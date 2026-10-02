using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shell;
using System.Windows.Threading;
using ClipboardManager.App.Platform;
using ClipboardManager.Core.Models;
using ClipboardManager.Core.Platform;
using ClipboardManager.Core.Services;

namespace ClipboardManager.App.UI;

public partial class QuickPasteWindow : Window
{
    private const int MaxResults = 300;

    private readonly ClipboardService _svc;
    private readonly IClipboardWriter _writer;
    private readonly WindowsPasteSimulator _paste;
    private readonly ObservableCollection<ItemViewModel> _items = new();
    private readonly DispatcherTimer _debounce;
    private readonly DispatcherTimer _tick;
    private readonly DispatcherTimer _trackForeground;
    private readonly DispatcherTimer _saveSizeDebounce;
    private bool _resizingFromCode;

    // A docked sidebar is always kept open.
    private bool KeepOpen => KeepOpenButton.IsChecked == true || Docked;
    private bool Docked => SidebarEdge != DockEdge.None;
    private bool IsPinnedFloating => !Docked && KeepOpenButton.IsChecked == true;
    private bool IsCompact => Docked || IsPinnedFloating;
    private const double PaletteWidth = 760, PaletteHeight = 520, SidebarWidth = 400;
    private const double PinnedWidth = 360, PinnedHeight = 440;
    private const double MinPinnedWidth = 280, MinPinnedHeight = 220;
    private const double MinPaletteWidth = 540, MinPaletteHeight = 360;

    private double TargetWidth => IsPinnedFloating
        ? (_svc.Settings.QuickPastePinnedWidth ?? PinnedWidth)
        : (_svc.Settings.QuickPasteWidth ?? PaletteWidth);

    private double TargetHeight => IsPinnedFloating
        ? (_svc.Settings.QuickPastePinnedHeight ?? PinnedHeight)
        : (_svc.Settings.QuickPasteHeight ?? PaletteHeight);

    private readonly AppBar _appBar;
    private bool _hiding;
    private long _markSequence;

    internal QuickPasteWindow(ClipboardService svc, IClipboardWriter writer, WindowsPasteSimulator paste)
    {
        _svc = svc;
        _writer = writer;
        _paste = paste;
        InitializeComponent();

        ItemsList.ItemsSource = _items;
        // Keep-open mode follows the app the user works in, so Enter pastes there.
        _trackForeground = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _trackForeground.Tick += (_, _) => _paste.RememberForegroundWindow(onlyIfPasteTarget: true);
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(70) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); Reload(); };

        // Live countdown for sensitive items while the palette is open.
        _tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _tick.Tick += (_, _) =>
        {
            foreach (var vm in _items)
            {
                if (vm.Item.IsSensitive) vm.Refresh();
            }
            if (ItemsList.SelectedItem is ItemViewModel sel) MetaExpires.Text = sel.MetaExpires;
        };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) _tick.Start(); else _tick.Stop();
            if (IsVisible && KeepOpen) _trackForeground.Start(); else _trackForeground.Stop();
        };

        SearchBox.TextChanged += (_, _) =>
        {
            Placeholder.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            _debounce.Stop();
            _debounce.Start();
        };
        ItemsList.SelectionChanged += (_, _) => UpdatePreview();
        ItemsList.MouseDoubleClick += (_, _) => PasteSelected();
        ItemsList.PreviewMouseRightButtonUp += (_, e) =>
        {
            if (ItemsList.SelectedItem is not null) { ShowTransformMenu(); e.Handled = true; }
        };
        PreviewContextMenu.Opened += (_, _) =>
        {
            bool hasSelection = PreviewText.SelectionLength > 0;
            MenuPasteSelection.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;
            MenuCopySelection.Header = hasSelection ? "Copy selection" : "Copy";
            MenuCopySelection.IsEnabled = hasSelection;
        };
        MenuPasteSelection.Click += (_, _) => PastePartialPreviewText();
        MenuCopySelection.Click += (_, _) => CopyPartialPreviewText();
        MenuCopyAll.Click += (_, _) => CopyAllPreviewText();
        MenuSelectAll.Click += (_, _) => { PreviewText.Focus(); PreviewText.SelectAll(); };
        PreviewKeyDown += OnPreviewKeyDown;
        Deactivated += (_, _) => { if (!KeepOpen) HidePalette(); };

        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(6),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
        });

        _saveSizeDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _saveSizeDebounce.Tick += (_, _) => { _saveSizeDebounce.Stop(); SaveSettings(); };
        SizeChanged += OnWindowSizeChanged;

        // "Keep open" / pin mode: the palette stays on screen (compact when floating, docked at screen edge) and pastes into the app you used last.
        KeepOpenButton.IsChecked = _svc.Settings.QuickPasteKeepOpen;
        KeepOpenButton.Checked += (_, _) => SetKeepOpen(true);
        KeepOpenButton.Unchecked += (_, _) => SetKeepOpen(false);

        // Borderless window: drag it by the search bar or the footer (the text box itself keeps normal mouse behavior).
        Header.MouseLeftButtonDown += OnDragAreaMouseDown;
        Footer.MouseLeftButtonDown += OnDragAreaMouseDown;

        // Sidebar: docked to a screen edge as an app bar (the shell keeps other windows out of that strip).
        _appBar = new AppBar(this, SidebarWidth);
        DockButton.Click += (_, _) => CycleDock();

        _resizingFromCode = true;
        try
        {
            ApplyLayout();
            if (IsPinnedFloating)
            {
                Width = TargetWidth;
                Height = TargetHeight;
                KeepOpenButton.ToolTip = "Unpin window (Ctrl+T)";
            }
            else if (!Docked)
            {
                Width = TargetWidth;
                Height = TargetHeight;
                KeepOpenButton.ToolTip = "Pin window / keep open (Ctrl+T)";
            }
        }
        finally
        {
            _resizingFromCode = false;
        }

        Closed += (_, _) => _appBar.Dispose();

        _svc.HistoryChanged += (_, _) => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (IsVisible) Reload(keepSelection: true);
        }));
    }

    public void TogglePalette()
    {
        if (IsVisible) HidePalette();
        else ShowPalette();
    }

    /// <param name="keepTarget">Reopening after our own dialog: keep pasting into the app that was active before.</param>
    public void ShowPalette(bool keepTarget = false, string search = "")
    {
        if (!keepTarget) _paste.RememberForegroundWindow();
        if (!Docked)
        {
            _resizingFromCode = true;
            try
            {
                ApplyLayout();
                Width = TargetWidth;
                Height = TargetHeight;
                PositionOnScreen();
            }
            finally
            {
                _resizingFromCode = false;
            }
        }
        SearchBox.Text = search;
        SearchBox.CaretIndex = search.Length;
        Reload();
        _hiding = false;
        Show();
        if (Docked) _appBar.Dock(SidebarEdge);
        Activate();
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
    }

    private void SetKeepOpen(bool on)
    {
        _svc.Settings.QuickPasteKeepOpen = on;
        SaveSettings();
        if (on && IsVisible) _trackForeground.Start(); else if (!KeepOpen) _trackForeground.Stop();

        if (!Docked)
        {
            _resizingFromCode = true;
            try
            {
                ApplyLayout();
                Width = on ? (_svc.Settings.QuickPastePinnedWidth ?? PinnedWidth) : (_svc.Settings.QuickPasteWidth ?? PaletteWidth);
                Height = on ? (_svc.Settings.QuickPastePinnedHeight ?? PinnedHeight) : (_svc.Settings.QuickPasteHeight ?? PaletteHeight);
                ClampToWorkArea();
                if (!on) UpdatePreview();
            }
            finally
            {
                _resizingFromCode = false;
            }
        }

        KeepOpenButton.ToolTip = on ? "Unpin window (Ctrl+T)" : "Pin window / keep open (Ctrl+T)";
        StatusText.Text = on ? "Pinned — Enter pastes into the app you used last" : $"{_svc.Count()} items";
    }

    private void SaveSettings()
    {
        try { _svc.Settings.Save(Path.Combine(_svc.DataFolder, "settings.json")); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public void HidePalette()
    {
        if (_hiding || !IsVisible) return;
        _hiding = true;
        foreach (var vm in _items) vm.IsMarked = false;
        _appBar.Undock(); // give the reserved strip back while hidden
        Hide();
        _hiding = false;
    }

    private DockEdge SidebarEdge =>
        Enum.TryParse<DockEdge>(_svc.Settings.SidebarEdge, ignoreCase: true, out var edge) ? edge : DockEdge.None;

    /// <summary>Ctrl+D / dock button: floating → right edge → left edge → floating.</summary>
    private void CycleDock()
    {
        var next = SidebarEdge switch
        {
            DockEdge.None => DockEdge.Right,
            DockEdge.Right => DockEdge.Left,
            _ => DockEdge.None,
        };
        _svc.Settings.SidebarEdge = next.ToString();
        SaveSettings();
        if (next == DockEdge.None)
        {
            _appBar.Undock();
            _resizingFromCode = true;
            try
            {
                ApplyLayout();
                Width = TargetWidth;
                Height = TargetHeight;
                PositionOnScreen();
            }
            finally
            {
                _resizingFromCode = false;
            }
            if (!KeepOpen) _trackForeground.Stop();
        }
        else
        {
            ApplyLayout();
            if (IsVisible) _appBar.Dock(next);
            if (IsVisible) _trackForeground.Start();
        }
        StatusText.Text = next == DockEdge.None
            ? (KeepOpenButton.IsChecked == true ? "Pinned — Enter pastes into the app you used last" : "Floating")
            : $"Docked {next.ToString().ToLowerInvariant()} — Enter pastes into the app you used last";
        SearchBox.Focus();
    }

    /// <summary>Sidebar or pinned = compact list only (no preview, no key hints); full floating = list + preview.</summary>
    private void ApplyLayout()
    {
        bool docked = Docked;
        bool compact = IsCompact;

        ResizeMode = docked ? ResizeMode.NoResize : ResizeMode.CanResize;
        if (docked)
        {
            MinWidth = SidebarWidth;
            MaxWidth = SidebarWidth;
            MinHeight = 0;
            MaxHeight = double.PositiveInfinity;
        }
        else if (compact)
        {
            MinWidth = MinPinnedWidth;
            MaxWidth = double.PositiveInfinity;
            MinHeight = MinPinnedHeight;
            MaxHeight = double.PositiveInfinity;
        }
        else
        {
            MinWidth = MinPaletteWidth;
            MaxWidth = double.PositiveInfinity;
            MinHeight = MinPaletteHeight;
            MaxHeight = double.PositiveInfinity;
        }

        ListColumn.Width = compact ? new GridLength(1, GridUnitType.Star) : new GridLength(340);
        SplitColumn.Width = new GridLength(compact ? 0 : 1);
        PreviewColumn.Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        PreviewPane.Visibility = PreviewSplit.Visibility = FooterHints.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        if (ResizeGripIndicator is not null)
            ResizeGripIndicator.Visibility = docked ? Visibility.Collapsed : Visibility.Visible;
        DockButton.Tag = docked ? "docked" : null;
        Placeholder.Text = IsPinnedFloating ? "Search clipboard…" : docked ? "Search clipboard…   F1: keys" : "Search clipboard…   type:sql  pinned:true  workspace:name   F1: keys";
        Header.ToolTip = Footer.ToolTip = docked ? null : "Drag to move";
    }

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!IsLoaded || Docked || _resizingFromCode) return;

        if (IsPinnedFloating)
        {
            _svc.Settings.QuickPastePinnedWidth = ActualWidth;
            _svc.Settings.QuickPastePinnedHeight = ActualHeight;
        }
        else
        {
            _svc.Settings.QuickPasteWidth = ActualWidth;
            _svc.Settings.QuickPasteHeight = ActualHeight;
        }
        _svc.Settings.QuickPasteLeft = Left;
        _svc.Settings.QuickPasteTop = Top;
        _saveSizeDebounce.Stop();
        _saveSizeDebounce.Start();
    }

    private void OnDragAreaMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed || Docked) return;
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            return; // mouse already released
        }
        // Remember where the user put it (also across restarts).
        _svc.Settings.QuickPasteLeft = Left;
        _svc.Settings.QuickPasteTop = Top;
        SaveSettings();
        SearchBox.Focus();
    }

    private void PositionOnScreen()
    {
        var s = _svc.Settings;
        if (s.QuickPasteLeft is double left && s.QuickPasteTop is double top && IsOnScreen(left, top))
        {
            Left = left;
            Top = top;
            ClampToWorkArea();
            return;
        }
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + Math.Max(40, (area.Height - Height) * 0.3);
    }

    private void ClampToWorkArea()
    {
        var area = SystemParameters.WorkArea;
        if (Left + Width > area.Right) Left = Math.Max(area.Left, area.Right - Width);
        if (Top + Height > area.Bottom) Top = Math.Max(area.Top, area.Bottom - Height);
        if (Left < area.Left) Left = area.Left;
        if (Top < area.Top) Top = area.Top;
    }

    /// <summary>True when at least the top-left 100×40 px of the window would be visible on some monitor.</summary>
    private static bool IsOnScreen(double left, double top)
    {
        double vl = SystemParameters.VirtualScreenLeft, vt = SystemParameters.VirtualScreenTop;
        double vw = SystemParameters.VirtualScreenWidth, vh = SystemParameters.VirtualScreenHeight;
        return left >= vl - 50 && top >= vt && left + 100 <= vl + vw && top + 40 <= vt + vh;
    }

    private void Reload(bool keepSelection = false)
    {
        long? selectedId = keepSelection && ItemsList.SelectedItem is ItemViewModel cur ? cur.Item.Id : null;
        var marks = _items.Where(i => i.IsMarked).ToDictionary(i => i.Item.Id, i => i.MarkOrder);

        var results = _svc.Search(SearchBox.Text, MaxResults);
        _items.Clear();
        foreach (var item in results)
        {
            _items.Add(new ItemViewModel(item, _svc)
            {
                IsMarked = marks.ContainsKey(item.Id),
                MarkOrder = marks.GetValueOrDefault(item.Id),
                Shortcut = _items.Count < 9 ? (_items.Count + 1).ToString() : "",
            });
        }

        var toSelect = selectedId is null ? null : _items.FirstOrDefault(i => i.Item.Id == selectedId);
        ItemsList.SelectedItem = toSelect ?? _items.FirstOrDefault();
        if (ItemsList.SelectedItem is not null) ItemsList.ScrollIntoView(ItemsList.SelectedItem);

        CountText.Text = SearchBox.Text.Length == 0 ? "" : $"{_items.Count} result{(_items.Count == 1 ? "" : "s")}";
        StatusText.Text = _svc.Settings.CaptureEnabled ? $"{_svc.Count()} items" : "Capture paused";
        EmptyText.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_items.Count == 0)
        {
            EmptyText.Text = SearchBox.Text.Length == 0 ? "Nothing copied yet. Copy something and it will appear here." : "No matches.";
            UpdatePreview();
        }
    }

    private void UpdatePreview()
    {
        if (ItemsList.SelectedItem is not ItemViewModel vm)
        {
            PreviewText.Text = "";
            PreviewImage.Source = null;
            PreviewImage.Visibility = Visibility.Collapsed;
            PreviewText.Visibility = Visibility.Visible;
            MetaType.Text = MetaSource.Text = MetaCopied.Text = MetaExpires.Text = "";
            return;
        }

        if (vm.IsImage)
        {
            PreviewImage.Source = vm.Image;
            PreviewImage.ToolTip = vm.Item.OcrText is { Length: > 0 } ocr ? "Text in image (Ctrl+Shift+Enter pastes it):\n" + ocr : null;
            PreviewImage.Visibility = Visibility.Visible;
            PreviewText.Visibility = Visibility.Collapsed;
        }
        else
        {
            PreviewText.Text = vm.PreviewText;
            PreviewText.FontFamily = vm.PreviewFont;
            PreviewText.ScrollToHome();
            PreviewImage.Source = null;
            PreviewImage.Visibility = Visibility.Collapsed;
            PreviewText.Visibility = Visibility.Visible;
        }
        MetaType.Text = vm.MetaType;
        MetaSource.Text = vm.MetaSource;
        MetaCopied.Text = vm.MetaCopied;
        MetaExpires.Text = vm.MetaExpires;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        switch (key)
        {
            case Key.Escape:
                HidePalette();
                e.Handled = true;
                break;
            case Key.Down when !PreviewText.IsKeyboardFocused:
                MoveSelection(+1);
                e.Handled = true;
                break;
            case Key.Up when !PreviewText.IsKeyboardFocused:
                MoveSelection(-1);
                e.Handled = true;
                break;
            case Key.PageDown when !PreviewText.IsKeyboardFocused:
                MoveSelection(+8);
                e.Handled = true;
                break;
            case Key.PageUp when !PreviewText.IsKeyboardFocused:
                MoveSelection(-8);
                e.Handled = true;
                break;
            case Key.Enter:
                if (PreviewText.IsKeyboardFocused && PreviewText.SelectionLength > 0)
                {
                    PastePartialPreviewText();
                }
                else
                {
                    PasteSelected(plainText: mods == (ModifierKeys.Control | ModifierKeys.Shift));
                }
                e.Handled = true;
                break;
            case >= Key.D1 and <= Key.D9 or >= Key.NumPad1 and <= Key.NumPad9
                when mods is ModifierKeys.Control or (ModifierKeys.Control | ModifierKeys.Shift):
                // Ctrl+1…9 pastes the n-th visible item directly (Ctrl+Shift = plain text).
                int n = key >= Key.NumPad1 ? key - Key.NumPad1 : key - Key.D1;
                if (n < _items.Count) PasteSelected(plainText: mods.HasFlag(ModifierKeys.Shift), target: _items[n]);
                e.Handled = true;
                break;
            case Key.P when mods == ModifierKeys.Control:
                TogglePinSelected();
                e.Handled = true;
                break;
            case Key.F1:
                ShowKeyHelp();
                e.Handled = true;
                break;
            case Key.D when mods == ModifierKeys.Control:
                CycleDock();
                e.Handled = true;
                break;
            case Key.S when mods == ModifierKeys.Control:
                StartPasteStack();
                e.Handled = true;
                break;
            case Key.N when mods == ModifierKeys.Control:
                EditSnippet(createNew: true);
                e.Handled = true;
                break;
            case Key.E when mods == ModifierKeys.Control:
                EditSnippet(createNew: false);
                e.Handled = true;
                break;
            case Key.W when mods == ModifierKeys.Control:
                CycleWorkspaceFilter();
                e.Handled = true;
                break;
            case Key.K when mods == ModifierKeys.Control:
                ShowTransformMenu();
                e.Handled = true;
                break;
            case Key.T when mods == ModifierKeys.Control:
                KeepOpenButton.IsChecked = !(KeepOpenButton.IsChecked == true);
                e.Handled = true;
                break;
            case Key.R when mods == ModifierKeys.Control:
                if (ItemsList.SelectedItem is ItemViewModel r) { r.Revealed = !r.Revealed; UpdatePreview(); }
                e.Handled = true;
                break;
            case Key.Space when mods == ModifierKeys.Control:
                if (ItemsList.SelectedItem is ItemViewModel m)
                {
                    m.IsMarked = !m.IsMarked;
                    m.MarkOrder = m.IsMarked ? ++_markSequence : 0;
                    MoveSelection(+1);
                    UpdateMarkedStatus();
                }
                e.Handled = true;
                break;
            case Key.C when mods == ModifierKeys.Control:
                if (PreviewText.IsKeyboardFocused && PreviewText.SelectionLength > 0)
                {
                    CopyPartialPreviewText();
                    e.Handled = true;
                }
                else if (SearchBox.SelectionLength == 0)
                {
                    CopySelected();
                    e.Handled = true;
                }
                break;
            case Key.Delete when mods == ModifierKeys.None && !PreviewText.IsKeyboardFocused && SearchBox.SelectionLength == 0 && SearchBox.CaretIndex == SearchBox.Text.Length:
                // At the end of the search text Delete would do nothing in the text box, so it deletes the item.
                DeleteSelected();
                e.Handled = true;
                break;
        }
    }

    private void MoveSelection(int delta)
    {
        if (_items.Count == 0) return;
        int i = Math.Clamp(ItemsList.SelectedIndex + delta, 0, _items.Count - 1);
        ItemsList.SelectedIndex = i;
        ItemsList.ScrollIntoView(ItemsList.SelectedItem);
    }

    private void UpdateMarkedStatus()
    {
        int n = _items.Count(i => i.IsMarked);
        StatusText.Text = n > 1 ? $"{n} selected — Enter pastes them merged" : n == 1 ? "1 selected" : $"{_svc.Count()} items";
    }

    /// <param name="plainText">Drop HTML/RTF formatting (Ctrl+Shift+Enter).</param>
    /// <param name="target">A specific item (Ctrl+1…9); null = the marked items, or the selection.</param>
    /// <param name="transform">Converts the text before it goes on the clipboard (Ctrl+K menu); implies plain text.</param>
    private bool WriteToClipboard(bool plainText = false, ItemViewModel? target = null, TextTransform? transform = null)
    {
        try
        {
            var marked = target is null ? _items.Where(i => i.IsMarked).ToList() : new();
            if (marked.Count > 1)
            {
                var merged = MergeService.Merge(marked.Select(m => m.Item), MergeSeparator.NewLine);
                _writer.Write(new ClipboardPayload
                {
                    Text = transform is null ? merged : transform.Apply(merged),
                    IsSensitive = marked.Any(m => m.Item.IsSensitive),
                });
                foreach (var m in marked) _svc.MarkUsed(m.Item);
                return true;
            }
            var vm = target ?? (marked.Count == 1 ? marked[0] : ItemsList.SelectedItem as ItemViewModel);
            if (vm is null) return false;
            var payload = _svc.LoadPayload(vm.Item, plainText || transform is not null, SnippetContext());
            if (payload is not null && transform is not null)
                payload = new ClipboardPayload { Text = transform.Apply(payload.Text ?? ""), IsSensitive = payload.IsSensitive };
            if (payload is null)
            {
                StatusText.Text = plainText ? "This item has no text to paste" : "The item's file is missing";
                return false;
            }
            _writer.Write(payload);
            _svc.MarkUsed(vm.Item);
            return true;
        }
        catch (COMException)
        {
            StatusText.Text = "Clipboard is busy — try again";
            return false;
        }
        catch (IOException)
        {
            StatusText.Text = "Could not read the item's file";
            return false;
        }
        catch (TransformException ex)
        {
            StatusText.Text = ex.Message;
            return false;
        }
    }

    private void PasteSelected(bool plainText = false, ItemViewModel? target = null, TextTransform? transform = null)
    {
        if (!WriteToClipboard(plainText, target, transform)) return;
        if (KeepOpen) ClearMarks(); else HidePalette();
        // Opened from the tray: there is no app to paste into, the item is just on the clipboard now.
        if (_paste.HasTarget) _paste.PasteIntoPreviousWindow();
    }

    private void CopySelected()
    {
        if (!WriteToClipboard()) return;
        if (KeepOpen) ClearMarks(); else HidePalette();
    }

    private void CopyPartialPreviewText()
    {
        var text = PreviewText.SelectedText;
        if (string.IsNullOrEmpty(text)) return;

        bool sensitive = (ItemsList.SelectedItem as ItemViewModel)?.Item.IsSensitive ?? false;
        try
        {
            _writer.Write(new ClipboardPayload { Text = text, IsSensitive = sensitive });
            StatusText.Text = "Copied selection to clipboard";
            if (!KeepOpen) HidePalette();
        }
        catch (COMException)
        {
            StatusText.Text = "Clipboard is busy — try again";
        }
    }

    private void PastePartialPreviewText()
    {
        var text = PreviewText.SelectedText;
        if (string.IsNullOrEmpty(text)) return;

        bool sensitive = (ItemsList.SelectedItem as ItemViewModel)?.Item.IsSensitive ?? false;
        try
        {
            _writer.Write(new ClipboardPayload { Text = text, IsSensitive = sensitive });
            if (KeepOpen) ClearMarks(); else HidePalette();
            if (_paste.HasTarget) _paste.PasteIntoPreviousWindow();
        }
        catch (COMException)
        {
            StatusText.Text = "Clipboard is busy — try again";
        }
    }

    private void CopyAllPreviewText()
    {
        var text = PreviewText.Text;
        if (string.IsNullOrEmpty(text)) return;

        bool sensitive = (ItemsList.SelectedItem as ItemViewModel)?.Item.IsSensitive ?? false;
        try
        {
            _writer.Write(new ClipboardPayload { Text = text, IsSensitive = sensitive });
            StatusText.Text = "Copied text to clipboard";
            if (!KeepOpen) HidePalette();
        }
        catch (COMException)
        {
            StatusText.Text = "Clipboard is busy — try again";
        }
    }

    private const string KeyHelp =
        "Enter               paste (merged if several are marked)\n" +
        "Ctrl+Shift+Enter    paste as plain text (an image: its text)\n" +
        "Ctrl+1 … 9          paste item 1 … 9 (Ctrl+Shift = plain)\n" +
        "Ctrl+K / right-click  transform: case, trim, JSON, SQL, Base64, URL\n" +
        "Ctrl+C              copy without pasting (or copies selection in preview)\n" +
        "Ctrl+Space          mark item (in order)\n" +
        "Ctrl+S              paste stack: each Ctrl+V pastes the next marked item\n" +
        "Ctrl+P              pin / unpin item\n" +
        "Ctrl+N / Ctrl+E     save as snippet / edit snippet\n" +
        "Ctrl+W              next workspace\n" +
        "Ctrl+T              pin window / keep open (compact size)\n" +
        "Ctrl+D              dock as sidebar: right → left → off\n" +
        "Ctrl+R              reveal a secret\n" +
        "Del                 delete item\n" +
        "Esc                 close\n\n" +
        "Search filters: type:sql  type:snippet  type:image  pinned:true  workspace:dev  after:2026-09-01";

    /// <summary>F1: the keyboard reference, shown in the preview pane (or the status line when docked or pinned).</summary>
    private void ShowKeyHelp()
    {
        if (IsCompact)
        {
            StatusText.Text = Docked
                ? "Keys: Ctrl+K transform · Ctrl+S paste stack · Ctrl+1…9 · Ctrl+D undock"
                : "Keys: Ctrl+K transform · Ctrl+S stack · Ctrl+1…9 · Ctrl+T unpin";
            return;
        }
        PreviewImage.Visibility = Visibility.Collapsed;
        PreviewText.Visibility = Visibility.Visible;
        PreviewText.FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas");
        PreviewText.Text = KeyHelp;
    }

    /// <summary>Ctrl+S: the marked items, in the order they were marked, become a paste stack (each Ctrl+V pastes the next).</summary>
    private void StartPasteStack()
    {
        var marked = _items.Where(i => i.IsMarked).OrderBy(i => i.MarkOrder).Select(i => i.Item).ToList();
        if (marked.Count == 0)
        {
            StatusText.Text = "Mark items with Ctrl+Space in the order to paste, then Ctrl+S";
            return;
        }
        if (PasteStackRequested is null) return;
        if (KeepOpen) ClearMarks(); else HidePalette();
        PasteStackRequested(marked);
    }

    /// <summary>Raised by Ctrl+S with the items to paste one by one.</summary>
    public event Action<IReadOnlyList<ClipboardItem>>? PasteStackRequested;

    /// <summary>Snippet variables: {clipboard} is what is on the Windows clipboard right now (falls back to the newest history text).</summary>
    private TemplateContext SnippetContext() => new()
    {
        Clipboard = () =>
        {
            try
            {
                if (System.Windows.Clipboard.ContainsText()) return System.Windows.Clipboard.GetText();
            }
            catch (COMException) { /* clipboard busy */ }
            return _svc.LatestHistoryText();
        },
    };

    /// <summary>Ctrl+N: new snippet from the selected item (or empty). Ctrl+E: edit the selected snippet.</summary>
    private void EditSnippet(bool createNew)
    {
        var vm = ItemsList.SelectedItem as ItemViewModel;
        bool editing = !createNew && vm?.Item.Kind == ContentKind.Snippet;
        if (!createNew && !editing)
        {
            StatusText.Text = "Select a snippet to edit (Ctrl+N saves the selection as a new snippet)";
            return;
        }
        if (createNew && vm is not null && (vm.IsImage || vm.Item.IsSensitive))
        {
            StatusText.Text = vm.IsImage ? "Images can't be snippets" : "Secrets can't be saved as snippets";
            return;
        }

        var (name, body) = vm is null ? ("", "") : ClipboardService.SnippetDraftFrom(vm.Item);
        var dialog = new SnippetDialog(_svc, name, body, editing ? vm!.Item : null);
        bool wasVisible = IsVisible;
        HidePalette();
        bool saved = dialog.ShowDialog() == true;
        if (saved || wasVisible) ShowPalette(keepTarget: true, search: saved ? "type:snippet " : "");
    }

    private static readonly System.Text.RegularExpressions.Regex WorkspaceFilterRx =
        new(@"(?:^|\s)(?:workspace|ws):(?:""(?<name>[^""]*)""|(?<name>\S+))", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>Ctrl+W: all workspaces → first → second → … → all, by editing the workspace: filter in the search text.</summary>
    private void CycleWorkspaceFilter()
    {
        var names = _svc.Workspaces().Select(w => w.Name).ToList();
        var text = SearchBox.Text;
        var match = WorkspaceFilterRx.Match(text);
        int index = match.Success ? names.FindIndex(n => n.Equals(match.Groups["name"].Value, StringComparison.OrdinalIgnoreCase)) : -1;
        string? next = index + 1 < names.Count ? names[index + 1] : null;

        var rest = (match.Success ? text.Remove(match.Index, match.Length) : text).Trim();
        var filter = next is null ? "" : "workspace:" + (next.Contains(' ') ? $"\"{next}\"" : next);
        var combined = string.Join(" ", new[] { filter, rest }.Where(s => s.Length > 0));
        SearchBox.Text = combined.Length > 0 ? combined + " " : "";
        SearchBox.CaretIndex = SearchBox.Text.Length;
    }

    /// <summary>Ctrl+K / right-click: paste the selection (or the marked items) converted by a text transform.</summary>
    private void ShowTransformMenu()
    {
        if (ItemsList.SelectedItem is not ItemViewModel vm) return;
        if (vm.IsImage && !_items.Any(i => i.IsMarked))
        {
            StatusText.Text = "Transforms work on text items";
            return;
        }

        var menu = new ContextMenu
        {
            PlacementTarget = ItemsList.ItemContainerGenerator.ContainerFromItem(vm) as UIElement ?? ItemsList,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
        };
        menu.Items.Add(new MenuItem { Header = "Paste as plain text", InputGestureText = "Ctrl+Shift+Enter", Tag = "plain" });
        menu.Items.Add(new Separator());
        foreach (var t in TextTransforms.All)
        {
            if (t.Id is "json-pretty" or "sql" or "base64-encode") menu.Items.Add(new Separator());
            menu.Items.Add(new MenuItem { Header = t.Name, Tag = t });
        }
        menu.AddHandler(MenuItem.ClickEvent, new RoutedEventHandler((_, e) =>
        {
            if (e.OriginalSource is not MenuItem mi) return;
            menu.IsOpen = false;
            if (mi.Tag is TextTransform t) PasteSelected(transform: t);
            else PasteSelected(plainText: true);
        }));
        menu.Closed += (_, _) => { if (IsVisible) SearchBox.Focus(); };
        menu.Opened += (_, _) => (menu.Items[0] as MenuItem)?.Focus();
        menu.IsOpen = true;
    }

    private void ClearMarks()
    {
        foreach (var vm in _items) vm.IsMarked = false;
        UpdateMarkedStatus();
    }

    private void TogglePinSelected()
    {
        if (ItemsList.SelectedItem is not ItemViewModel vm) return;
        _svc.TogglePin(vm.Item); // raises HistoryChanged → Reload(keepSelection)
    }

    private void DeleteSelected()
    {
        if (ItemsList.SelectedItem is not ItemViewModel vm) return;
        int index = ItemsList.SelectedIndex;
        _svc.Delete(vm.Item);
        if (_items.Count > 0) ItemsList.SelectedIndex = Math.Clamp(index, 0, _items.Count - 1);
    }
}
