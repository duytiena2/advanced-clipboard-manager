using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using ClipboardManager.App.Platform;
using ClipboardManager.Core.Models;
using ClipboardManager.Core.Platform;
using ClipboardManager.Core.Search;
using ClipboardManager.Core.Services;

namespace ClipboardManager.App.UI;

public partial class QuickPasteWindow : Window
{
    private const int MaxResults = 300;

    private readonly ClipboardService _svc;
    private readonly IClipboardWriter _writer;
    private readonly WindowsPasteSimulator _paste;
    private readonly ObservableCollection<ItemViewModel> _items = new();
    private readonly ObservableCollection<SearchFilterChip> _activeChips = new();
    private bool _isUpdatingSearchBoxText;
    private readonly DispatcherTimer _debounce;
    private readonly DispatcherTimer _tick;
    private readonly DispatcherTimer _trackForeground;
    private readonly DispatcherTimer _saveSizeDebounce;
    private bool _resizingFromCode;
    private bool _layoutIsCompact;

    // A docked sidebar is always kept open.
    private bool KeepOpen => KeepOpenButton.IsChecked == true || Docked;
    private bool Docked => SidebarEdge != DockEdge.None;
    private bool IsPinnedFloating => !Docked && KeepOpenButton.IsChecked == true;
    private const double FullLayoutBreakpoint = 540;
    private bool IsCompact => Docked || (ActualWidth > 0 ? ActualWidth : (!double.IsNaN(Width) && Width > 0 ? Width : TargetWidth)) < FullLayoutBreakpoint;
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
        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(_items);
        view.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription(nameof(ItemViewModel.SectionHeader)));
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

        FilterChipsItemsControl.ItemsSource = _activeChips;
        _activeChips.CollectionChanged += (_, _) =>
        {
            UpdateSearchChipsState();
            UpdateWorkspaceButtonText();
        };

        SearchBox.TextChanged += OnSearchBoxTextChanged;

        SearchBox.PreviewMouseLeftButtonDown += (_, _) =>
        {
            if (!SearchSuggestionsPopup.IsOpen)
            {
                RefreshSuggestionsPopup();
                SearchSuggestionsPopup.IsOpen = true;
            }
        };

        SearchContainer.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is not Button)
            {
                SearchBox.Focus();
                if (!SearchSuggestionsPopup.IsOpen)
                {
                    RefreshSuggestionsPopup();
                    SearchSuggestionsPopup.IsOpen = true;
                }
            }
        };
        ItemsList.SelectionChanged += (_, _) => UpdatePreview();
        ItemsList.MouseDoubleClick += (_, _) => PasteSelected();
        ItemsList.PreviewMouseRightButtonUp += (_, e) =>
        {
            if (ItemsList.SelectedItem is not null) { ShowTransformMenu(); e.Handled = true; }
        };
        PreviewCode.ContextMenu = PreviewContextMenu;
        OcrTextBox.ContextMenu = PreviewContextMenu;
        PreviewContextMenu.Opened += (_, _) =>
        {
            bool hasSelection = HasPreviewSelection;
            MenuPasteSelection.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;
            MenuCopySelection.Header = hasSelection ? "Copy selection" : "Copy";
            MenuCopySelection.IsEnabled = hasSelection;
        };
        MenuPasteSelection.Click += (_, _) => PastePartialPreviewText();
        MenuCopySelection.Click += (_, _) => CopyPartialPreviewText();
        MenuCopyAll.Click += (_, _) => CopyAllPreviewText();
        MenuSelectAll.Click += (_, _) => SelectAllPreviewText();

        SensitiveRevealButton.Click += (_, _) => ToggleRevealSelected();
        SensitiveHideButton.Click += (_, _) => ToggleRevealSelected();
        SensitiveHideTextButton.Click += (_, _) => ToggleRevealSelected();
        ToggleRevealButton.Click += (_, _) => ToggleRevealSelected();

        UrlLaunchButton.Click += (_, _) => LaunchCurrentUrl();
        OpenUrlButton.Click += (_, _) => LaunchCurrentUrl();
        UrlCopyButton.Click += (_, _) => CopyAllPreviewText();

        ToggleOcrButton.Click += (_, _) =>
        {
            OcrTextPanel.Visibility = OcrTextPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            ToggleOcrButton.Content = OcrTextPanel.Visibility == Visibility.Visible ? "Hide OCR text" : "View OCR text";
        };
        CopyOcrButton.Click += (_, _) =>
        {
            if (!string.IsNullOrEmpty(OcrTextBox.Text))
            {
                try
                {
                    _writer.Write(new ClipboardPayload { Text = OcrTextBox.Text });
                    StatusText.Text = "Copied OCR text";
                }
                catch (COMException) { StatusText.Text = "Clipboard is busy — try again"; }
            }
        };

        FormatButton.Click += (_, _) => FormatSelectedCode();
        CopyPreviewButton.Click += (_, _) => CopyAllPreviewText();
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
        if (DragBar is not null) DragBar.MouseLeftButtonDown += OnDragAreaMouseDown;

        // Sidebar: docked to a screen edge as an app bar (the shell keeps other windows out of that strip).
        _appBar = new AppBar(this, SidebarWidth);
        DockButton.Click += (_, _) => CycleDock();
        SettingsButton.Click += (_, _) => OpenSettings();
        if (HelpButton is not null) HelpButton.Click += (_, _) => ShowKeyHelp();
        if (WorkspaceSelectorButton is not null)
        {
            WorkspaceSelectorButton.Click += (_, _) => ShowWorkspaceMenu(WorkspaceSelectorButton);
            WorkspaceSelectorButton.MouseRightButtonUp += (_, e) => { e.Handled = true; CycleWorkspaceFilter(); };
        }
        if (TransparencyButton is not null) TransparencyButton.Click += (_, _) => ToggleTransparency();
        if (WidgetModeButton is not null) WidgetModeButton.Click += (_, _) => ToggleWidgetMode();
        if (PreviewSplit is not null)
        {
            PreviewSplit.DragDelta += OnSplitterDragDelta;
            PreviewSplit.DragCompleted += OnSplitterDragCompleted;
            PreviewSplit.MouseDoubleClick += (_, _) => CycleSplitRatio();
            PreviewSplit.MouseRightButtonUp += (s, e) => { e.Handled = true; ShowSplitContextMenu(PreviewSplit); };
        }
        if (SplitPresetButton is not null) SplitPresetButton.Click += (_, _) => ShowSplitContextMenu(SplitPresetButton);
        if (StopPasteStackButton is not null) StopPasteStackButton.Click += (_, _) => StopPasteStackRequested?.Invoke();
        if (HelpCloseButton is not null) HelpCloseButton.Click += (_, _) => HideHelpOverlay();
        if (HelpSearchBox is not null) HelpSearchBox.TextChanged += (_, _) => FilterHelpShortcuts(HelpSearchBox.Text);
        UpdateSearchChipsState();
        ItemsList.SelectionChanged += (_, _) => UpdateContextualToolbar();
        Loaded += (_, _) => ApplyTransparency();

        _resizingFromCode = true;
        try
        {
            if (!Docked)
            {
                Width = TargetWidth;
                Height = TargetHeight;
            }
            ApplyLayout();
            if (IsPinnedFloating)
            {
                KeepOpenButton.ToolTip = "Unpin window (Ctrl+T)";
            }
            else if (!Docked)
            {
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
        _resizingFromCode = true;
        try
        {
            Width = TargetWidth;
            Height = TargetHeight;
            ApplyLayout();
            if (Docked)
            {
                var area = SystemParameters.WorkArea;
                if (SidebarEdge == DockEdge.Right)
                {
                    Left = Math.Max(area.Left, area.Right - Width - 16);
                    Top = Math.Max(area.Top, area.Top + Math.Min(60, (area.Height - Height) / 2));
                }
                else if (SidebarEdge == DockEdge.Left)
                {
                    Left = area.Left + 16;
                    Top = Math.Max(area.Top, area.Top + Math.Min(60, (area.Height - Height) / 2));
                }
            }
            else
            {
                PositionOnScreen();
            }
            if (!IsCompact) UpdatePreview();
        }
        finally
        {
            _resizingFromCode = false;
        }

        _activeChips.Clear();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var (extractedChips, remaining) = SearchFilterChip.ExtractFilters(search);
            foreach (var c in extractedChips) _activeChips.Add(c);
            _isUpdatingSearchBoxText = true;
            try
            {
                SearchBox.Text = remaining;
                SearchBox.CaretIndex = remaining.Length;
            }
            finally
            {
                _isUpdatingSearchBoxText = false;
            }
        }
        else
        {
            _isUpdatingSearchBoxText = true;
            try
            {
                SearchBox.Text = "";
                SearchBox.CaretIndex = 0;
            }
            finally
            {
                _isUpdatingSearchBoxText = false;
            }
        }
        if (SearchSuggestionsPopup is not null) SearchSuggestionsPopup.IsOpen = false;
        Reload();
        _hiding = false;
        Show();
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
                Width = on ? (_svc.Settings.QuickPastePinnedWidth ?? PinnedWidth) : (_svc.Settings.QuickPasteWidth ?? PaletteWidth);
                Height = on ? (_svc.Settings.QuickPastePinnedHeight ?? PinnedHeight) : (_svc.Settings.QuickPasteHeight ?? PaletteHeight);
                ApplyLayout();
                ClampToWorkArea();
                if (!IsCompact) UpdatePreview();
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
        if (SearchSuggestionsPopup is not null) SearchSuggestionsPopup.IsOpen = false;
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

        // Release any desktop AppBar lock so window can move freely anytime
        _appBar.Undock();

        _resizingFromCode = true;
        try
        {
            ApplyLayout();
            Width = TargetWidth;
            Height = TargetHeight;

            var area = SystemParameters.WorkArea;
            if (next == DockEdge.Right)
            {
                Left = Math.Max(area.Left, area.Right - Width - 16);
                Top = Math.Max(area.Top, area.Top + Math.Min(60, (area.Height - Height) / 2));
            }
            else if (next == DockEdge.Left)
            {
                Left = area.Left + 16;
                Top = Math.Max(area.Top, area.Top + Math.Min(60, (area.Height - Height) / 2));
            }
            else
            {
                PositionOnScreen();
            }
            if (!IsCompact) UpdatePreview();
        }
        finally
        {
            _resizingFromCode = false;
        }

        DockButton.Tag = next != DockEdge.None ? "docked" : null;
        StatusText.Text = next == DockEdge.None
            ? (KeepOpenButton.IsChecked == true ? "Pinned — Enter pastes into the app you used last" : "Floating")
            : $"Snapped {next.ToString().ToLowerInvariant()} — kéo để di chuyển tự do";
        SearchBox.Focus();
    }


    /// <summary>Sidebar or narrow floating (&lt; 540) = compact list only; wide floating (&gt;= 540) = full list + preview.</summary>
    private void ApplyLayout()
    {
        bool docked = Docked;
        bool compact = IsCompact;
        _layoutIsCompact = compact;

        ResizeMode = docked ? ResizeMode.NoResize : ResizeMode.CanResize;
        if (docked)
        {
            MinWidth = SidebarWidth;
            MaxWidth = SidebarWidth;
            MinHeight = 0;
            MaxHeight = double.PositiveInfinity;
        }
        else
        {
            MinWidth = MinPinnedWidth;
            MaxWidth = double.PositiveInfinity;
            MinHeight = MinPinnedHeight;
            MaxHeight = double.PositiveInfinity;
        }

        if (compact)
        {
            ListColumn.Width = new GridLength(1, GridUnitType.Star);
            SplitColumn.Width = new GridLength(0);
            PreviewColumn.Width = new GridLength(0);
            if (PreviewSplit is not null) PreviewSplit.Visibility = Visibility.Collapsed;
            if (SplitPresetButton is not null) SplitPresetButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            double ratio = _svc.Settings.QuickPasteListRatio ?? 0.40;
            ratio = Math.Clamp(ratio, 0.20, 0.80);
            ListColumn.Width = new GridLength(ratio * 100, GridUnitType.Star);
            SplitColumn.Width = new GridLength(5);
            PreviewColumn.Width = new GridLength((1.0 - ratio) * 100, GridUnitType.Star);
            if (PreviewSplit is not null) PreviewSplit.Visibility = Visibility.Visible;
            if (SplitPresetButton is not null) SplitPresetButton.Visibility = Visibility.Visible;
        }
        PreviewPane.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        UpdateContextualToolbar();
        if (ResizeGripIndicator is not null)
            ResizeGripIndicator.Visibility = docked ? Visibility.Collapsed : Visibility.Visible;
        DockButton.Tag = docked ? "docked" : null;
        if (WidgetModeButton is not null) WidgetModeButton.Tag = compact ? "compact" : null;
        Placeholder.Text = "Search clipboard…";
        Header.ToolTip = Footer.ToolTip = docked ? null : "Drag to move";
    }

    private static readonly (double ratio, string label, string description)[] SplitPresets =
    [
        (0.40, "40% / 60%", "Tìm kiếm nhanh • Preview nhỏ"),
        (0.50, "50% / 50%", "Cân bằng 1:1"),
        (0.30, "30% / 70%", "Mở rộng Preview"),
        (0.25, "25% / 75%", "Xem screenshot & code • Preview lớn"),
    ];

    private void SetPaneRatio(double listRatio)
    {
        if (IsCompact) return;
        listRatio = Math.Clamp(listRatio, 0.20, 0.80);
        _svc.Settings.QuickPasteListRatio = listRatio;
        ListColumn.Width = new GridLength(listRatio * 100, GridUnitType.Star);
        PreviewColumn.Width = new GridLength((1.0 - listRatio) * 100, GridUnitType.Star);
        _saveSizeDebounce.Stop();
        _saveSizeDebounce.Start();
    }

    private void CycleSplitRatio()
    {
        if (IsCompact) return;
        double current = _svc.Settings.QuickPasteListRatio ?? 0.40;
        int idx = -1;
        for (int i = 0; i < SplitPresets.Length; i++)
        {
            if (Math.Abs(SplitPresets[i].ratio - current) < 0.04)
            {
                idx = i;
                break;
            }
        }
        int nextIdx = (idx + 1) % SplitPresets.Length;
        SetPaneRatio(SplitPresets[nextIdx].ratio);
        StatusText.Text = $"Tỷ lệ: {SplitPresets[nextIdx].label} ({SplitPresets[nextIdx].description})";
    }

    private void ShowSplitContextMenu(UIElement target)
    {
        var menu = new ContextMenu();
        double current = _svc.Settings.QuickPasteListRatio ?? 0.40;

        foreach (var (ratio, label, desc) in SplitPresets)
        {
            var item = new MenuItem
            {
                Header = $"{label}  —  {desc}",
                IsChecked = Math.Abs(ratio - current) < 0.04,
            };
            double r = ratio;
            item.Click += (_, _) =>
            {
                SetPaneRatio(r);
                StatusText.Text = $"Tỷ lệ: {label} ({desc})";
            };
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());

        var resetItem = new MenuItem { Header = "Mặc định (40% / 60%)" };
        resetItem.Click += (_, _) =>
        {
            SetPaneRatio(0.40);
            StatusText.Text = "Tỷ lệ: Mặc định (40% / 60%)";
        };
        menu.Items.Add(resetItem);

        menu.PlacementTarget = target;
        menu.IsOpen = true;
    }

    private void OnSplitterDragDelta(object sender, DragDeltaEventArgs e)
    {
        double total = ListColumn.ActualWidth + PreviewColumn.ActualWidth;
        if (total > 50)
        {
            double ratio = ListColumn.ActualWidth / total;
            StatusText.Text = $"Tỷ lệ: {ratio:P0} / {1.0 - ratio:P0}";
        }
    }

    private void OnSplitterDragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (IsCompact) return;
        double total = ListColumn.ActualWidth + PreviewColumn.ActualWidth;
        if (total > 50)
        {
            double ratio = ListColumn.ActualWidth / total;
            ratio = Math.Clamp(ratio, 0.20, 0.80);
            ListColumn.Width = new GridLength(ratio * 100, GridUnitType.Star);
            PreviewColumn.Width = new GridLength((1.0 - ratio) * 100, GridUnitType.Star);
            _svc.Settings.QuickPasteListRatio = ratio;
            _saveSizeDebounce.Stop();
            _saveSizeDebounce.Start();
            StatusText.Text = $"Tỷ lệ: {ratio:P0} / {1.0 - ratio:P0}";
        }
    }

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!IsLoaded || Docked || _resizingFromCode) return;

        bool compact = ActualWidth < FullLayoutBreakpoint;
        if (compact != _layoutIsCompact)
        {
            ApplyLayout();
            if (!compact)
            {
                UpdatePreview();
            }
        }

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

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    private Point GetCursorScreenPosition()
    {
        if (GetCursorPos(out var pt))
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            return new Point(pt.X / dpi.DpiScaleX, pt.Y / dpi.DpiScaleY);
        }
        return new Point(Left, Top);
    }

    private void OnDragAreaMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (Docked)
        {
            UndockAndStartDrag();
            return;
        }
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

    private void UndockAndStartDrag()
    {
        _appBar.Undock();
        _svc.Settings.SidebarEdge = "None";
        DockButton.Tag = null;
        _resizingFromCode = true;
        try
        {
            ApplyLayout();
            Width = TargetWidth;
            Height = TargetHeight;
            var pt = GetCursorScreenPosition();
            Left = Math.Max(0, pt.X - Width / 2);
            Top = Math.Max(0, pt.Y - 20);
        }
        finally
        {
            _resizingFromCode = false;
        }
        try
        {
            DragMove();
        }
        catch (InvalidOperationException) { }
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

        string effectiveQuery = SearchFilterChip.Combine(_activeChips, SearchBox.Text);
        var results = _svc.Search(effectiveQuery, MaxResults);
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

        CountText.Text = string.IsNullOrWhiteSpace(effectiveQuery) ? "" : $"{_items.Count} result{(_items.Count == 1 ? "" : "s")}";
        StatusText.Text = _svc.Settings.CaptureEnabled ? $"{_svc.Count()} items" : "Capture paused";
        if (_items.Count == 0)
        {
            if (string.IsNullOrWhiteSpace(effectiveQuery))
            {
                if (EmptyClipboardContainer is not null) EmptyClipboardContainer.Visibility = Visibility.Visible;
                if (EmptySearchContainer is not null) EmptySearchContainer.Visibility = Visibility.Collapsed;
            }
            else
            {
                if (EmptyClipboardContainer is not null) EmptyClipboardContainer.Visibility = Visibility.Collapsed;
                if (EmptySearchContainer is not null) EmptySearchContainer.Visibility = Visibility.Visible;
            }
            UpdatePreview();
        }
        else
        {
            if (EmptyClipboardContainer is not null) EmptyClipboardContainer.Visibility = Visibility.Collapsed;
            if (EmptySearchContainer is not null) EmptySearchContainer.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdatePreview()
    {
        UpdateContextualToolbar();
        if (ItemsList.SelectedItem is not ItemViewModel vm)
        {
            PreviewHeaderBar.Visibility = Visibility.Collapsed;
            SensitiveHiddenContainer.Visibility = Visibility.Collapsed;
            SensitiveRevealedBanner.Visibility = Visibility.Collapsed;
            SensitiveRevealedTextBanner.Visibility = Visibility.Collapsed;
            ImageContainer.Visibility = Visibility.Collapsed;
            UrlContainer.Visibility = Visibility.Collapsed;
            CodeContainer.Visibility = Visibility.Collapsed;
            TextContainer.Visibility = Visibility.Collapsed;
            PreviewImage.Source = null;
            MetaType.Text = MetaSource.Text = MetaCopied.Text = MetaExpires.Text = "";
            return;
        }

        if (EmptyClipboardContainer is not null) EmptyClipboardContainer.Visibility = Visibility.Collapsed;
        if (EmptySearchContainer is not null) EmptySearchContainer.Visibility = Visibility.Collapsed;
        PreviewHeaderBar.Visibility = Visibility.Visible;

        // Reset action buttons
        FormatButton.Visibility = Visibility.Collapsed;
        OpenUrlButton.Visibility = Visibility.Collapsed;
        ToggleRevealButton.Visibility = Visibility.Collapsed;

        // Populate bottom metadata table
        MetaType.Text = vm.MetaType;
        MetaSource.Text = vm.MetaSource;
        MetaCopied.Text = vm.MetaCopied;
        MetaExpires.Text = vm.MetaExpires;

        // Hide all view containers before showing the active one
        SensitiveHiddenContainer.Visibility = Visibility.Collapsed;
        SensitiveRevealedBanner.Visibility = Visibility.Collapsed;
        SensitiveRevealedTextBanner.Visibility = Visibility.Collapsed;
        ImageContainer.Visibility = Visibility.Collapsed;
        UrlContainer.Visibility = Visibility.Collapsed;
        CodeContainer.Visibility = Visibility.Collapsed;
        TextContainer.Visibility = Visibility.Collapsed;

        // 1. Sensitive content
        if (vm.IsSensitive)
        {
            SetPreviewBadge("🔒 SENSITIVE", "#FEF2F2", "#DC2626");

            if (!vm.Revealed)
            {
                SensitiveHiddenContainer.Visibility = Visibility.Visible;
                SensitiveSubtypeText.Text = $"{vm.SecretTypeName} detected";
                SensitiveMaskedText.Text = vm.MaskedSecret;
                SensitiveExpiryText.Text = vm.MetaExpires;
                PreviewStatsText.Text = "Hidden · Press Ctrl+R to reveal";

                ToggleRevealButton.Visibility = Visibility.Visible;
                ToggleRevealButton.Content = "Reveal (Ctrl+R)";
            }
            else
            {
                PreviewStatsText.Text = $"{vm.SecretTypeName} · Revealed";
                ToggleRevealButton.Visibility = Visibility.Visible;
                ToggleRevealButton.Content = "Hide (Ctrl+R)";

                if (vm.IsCode)
                {
                    CodeContainer.Visibility = Visibility.Visible;
                    SensitiveRevealedBanner.Visibility = Visibility.Visible;
                    PreviewCode.Document = SyntaxHighlighter.CreateDocument(vm.PreviewText, vm.Item.Subtype);
                    PreviewCode.ScrollToHome();
                }
                else
                {
                    TextContainer.Visibility = Visibility.Visible;
                    SensitiveRevealedTextBanner.Visibility = Visibility.Visible;
                    PreviewText.FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas");
                    PreviewText.Text = vm.PreviewText;
                    PreviewText.ScrollToHome();
                }
            }
            return;
        }

        // 2. Image
        if (vm.IsImage)
        {
            ImageContainer.Visibility = Visibility.Visible;
            SetPreviewBadge("IMAGE", "#F3E8FF", "#7C3AED");

            string dimStr = vm.ImageResolutionText;
            ImageDimensionsText.Text = dimStr;
            PreviewStatsText.Text = $"{dimStr} · {vm.MetaSize}";

            PreviewImage.Source = vm.Image;
            PreviewImage.ToolTip = vm.Item.OcrText is { Length: > 0 } ocr ? "Text in image (Ctrl+Shift+Enter pastes it):\n" + ocr : null;

            if (vm.HasOcr)
            {
                OcrBadgeBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E6F4F1"));
                OcrBadgeText.Foreground = (Brush)FindResource("Accent");
                OcrBadgeText.Text = "OCR: Available";
                ToggleOcrButton.Visibility = Visibility.Visible;
                OcrTextBox.Text = vm.Item.OcrText;
            }
            else
            {
                OcrBadgeBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F1F3F5"));
                OcrBadgeText.Foreground = (Brush)FindResource("Muted");
                OcrBadgeText.Text = vm.OcrStatusText;
                ToggleOcrButton.Visibility = Visibility.Collapsed;
                OcrTextPanel.Visibility = Visibility.Collapsed;
            }
            return;
        }

        // 3. URL
        if (vm.IsUrl)
        {
            UrlContainer.Visibility = Visibility.Visible;
            SetPreviewBadge("🔗 " + vm.UrlTitle.ToUpperInvariant(), "#E6F4F1", "#0E6B68");
            PreviewStatsText.Text = vm.UrlDomain;

            var rawUrl = (vm.Item.TextContent ?? "").Trim();
            UrlFullText.Text = rawUrl;
            UrlSiteName.Text = vm.UrlTitle;
            UrlHostText.Text = vm.UrlDomain;
            OpenUrlButton.Visibility = Visibility.Visible;

            var cand = rawUrl.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? "https://" + rawUrl : rawUrl;
            if (Uri.TryCreate(cand, UriKind.Absolute, out var uri))
            {
                UrlSchemeVal.Text = uri.Scheme;
                UrlHostVal.Text = uri.Host;
                UrlPathVal.Text = string.IsNullOrEmpty(uri.PathAndQuery) ? "/" : uri.PathAndQuery;
            }
            else
            {
                UrlSchemeVal.Text = "—";
                UrlHostVal.Text = vm.UrlDomain;
                UrlPathVal.Text = rawUrl;
            }
            return;
        }

        // 4. Code (SQL, JSON, XML, YAML, Shell, etc.)
        if (vm.IsCode)
        {
            CodeContainer.Visibility = Visibility.Visible;
            SetPreviewBadge(vm.CodeLanguage.ToUpperInvariant(), "#E8F2FF", "#0969DA");
            PreviewStatsText.Text = vm.CodeStats;

            if (vm.Item.Subtype.Equals("sql", StringComparison.OrdinalIgnoreCase) ||
                vm.Item.Subtype.Equals("json", StringComparison.OrdinalIgnoreCase))
            {
                FormatButton.Visibility = Visibility.Visible;
            }

            PreviewCode.Document = SyntaxHighlighter.CreateDocument(vm.PreviewText, vm.Item.Subtype);
            PreviewCode.ScrollToHome();
            return;
        }

        // 5. Plain text / Markdown / Snippet / other
        TextContainer.Visibility = Visibility.Visible;
        SetPreviewBadge(vm.TypeLabel.ToUpperInvariant(), "#F3F4F6", "#4B5563");
        PreviewStatsText.Text = $"{vm.TextLength} characters · {vm.LineCount} lines";

        PreviewText.Text = vm.PreviewText;
        PreviewText.FontFamily = vm.PreviewFont;
        PreviewText.ScrollToHome();
    }

    private void SetPreviewBadge(string text, string bgHex, string fgHex)
    {
        PreviewBadgeText.Text = text;
        PreviewBadgeBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bgHex));
        PreviewBadgeText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fgHex));
    }

    private void ToggleRevealSelected()
    {
        if (ItemsList.SelectedItem is ItemViewModel r)
        {
            r.Revealed = !r.Revealed;
            UpdatePreview();
        }
    }

    private void LaunchCurrentUrl()
    {
        if (ItemsList.SelectedItem is ItemViewModel vm && !string.IsNullOrEmpty(vm.Item.TextContent))
        {
            var raw = vm.Item.TextContent.Trim();
            if (raw.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) raw = "https://" + raw;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(raw) { UseShellExecute = true });
            }
            catch { }
        }
    }

    private void FormatSelectedCode()
    {
        if (ItemsList.SelectedItem is ItemViewModel vm && !string.IsNullOrEmpty(vm.Item.TextContent))
        {
            var sub = vm.Item.Subtype.ToLowerInvariant();
            try
            {
                string formatted = sub switch
                {
                    "sql" => TextTransforms.Apply("sql", vm.Item.TextContent),
                    "json" => TextTransforms.Apply("json-pretty", vm.Item.TextContent),
                    _ => vm.Item.TextContent
                };
                PreviewCode.Document = SyntaxHighlighter.CreateDocument(formatted, sub);
                StatusText.Text = $"Formatted {vm.CodeLanguage}";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Format error: {ex.Message}";
            }
        }
    }

    private bool HasPreviewSelection =>
        (PreviewText.IsKeyboardFocused && PreviewText.SelectionLength > 0) ||
        (PreviewCode.IsKeyboardFocused && !PreviewCode.Selection.IsEmpty) ||
        (UrlFullText.IsKeyboardFocused && UrlFullText.SelectionLength > 0) ||
        (OcrTextBox.IsKeyboardFocused && OcrTextBox.SelectionLength > 0);

    private string GetPreviewSelectedText()
    {
        if (PreviewText.IsKeyboardFocused && PreviewText.SelectionLength > 0)
            return PreviewText.SelectedText;
        if (PreviewCode.IsKeyboardFocused && !PreviewCode.Selection.IsEmpty)
            return PreviewCode.Selection.Text;
        if (UrlFullText.IsKeyboardFocused && UrlFullText.SelectionLength > 0)
            return UrlFullText.SelectedText;
        if (OcrTextBox.IsKeyboardFocused && OcrTextBox.SelectionLength > 0)
            return OcrTextBox.SelectedText;
        return "";
    }

    private bool IsAnyPreviewFocused =>
        PreviewText.IsKeyboardFocused || PreviewCode.IsKeyboardFocused ||
        UrlFullText.IsKeyboardFocused || OcrTextBox.IsKeyboardFocused;

    private string GetCurrentPreviewFullText()
    {
        if (ItemsList.SelectedItem is not ItemViewModel vm) return "";
        if (vm.IsImage) return vm.Item.OcrText ?? "";
        return vm.Item.TextContent ?? "";
    }

    private void SelectAllPreviewText()
    {
        if (CodeContainer.Visibility == Visibility.Visible)
        {
            PreviewCode.Focus();
            PreviewCode.SelectAll();
        }
        else if (UrlContainer.Visibility == Visibility.Visible)
        {
            UrlFullText.Focus();
            UrlFullText.SelectAll();
        }
        else if (OcrTextPanel.Visibility == Visibility.Visible && OcrTextBox.IsKeyboardFocused)
        {
            OcrTextBox.Focus();
            OcrTextBox.SelectAll();
        }
        else
        {
            PreviewText.Focus();
            PreviewText.SelectAll();
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        switch (key)
        {
            case Key.Escape:
                if (HelpOverlay is not null && HelpOverlay.Visibility == Visibility.Visible)
                {
                    HideHelpOverlay();
                    e.Handled = true;
                    break;
                }
                if (SearchSuggestionsPopup is not null && SearchSuggestionsPopup.IsOpen)
                {
                    SearchSuggestionsPopup.IsOpen = false;
                    e.Handled = true;
                    break;
                }
                if (PasteStackBanner is not null && PasteStackBanner.Visibility == Visibility.Visible)
                {
                    StopPasteStackRequested?.Invoke();
                    e.Handled = true;
                    break;
                }
                if (_items.Any(i => i.IsMarked))
                {
                    ClearMarks();
                    e.Handled = true;
                    break;
                }
                HidePalette();
                e.Handled = true;
                break;
            case Key.Back when SearchBox.IsKeyboardFocused && SearchBox.SelectionLength == 0 && SearchBox.CaretIndex == 0 && _activeChips.Count > 0:
                _activeChips.RemoveAt(_activeChips.Count - 1);
                Reload();
                e.Handled = true;
                break;
            case (Key.Enter or Key.Tab) when SearchBox.IsKeyboardFocused && SearchFilterChip.TryParseFilter(SearchBox.Text.Trim(), out var chip) && chip is not null:
                AddOrReplaceChip(chip);
                _isUpdatingSearchBoxText = true;
                try
                {
                    SearchBox.Text = "";
                    SearchBox.CaretIndex = 0;
                }
                finally
                {
                    _isUpdatingSearchBoxText = false;
                }
                Reload();
                e.Handled = true;
                break;
            case Key.Down when SearchBox.IsKeyboardFocused && SearchSuggestionsPopup is not null && !SearchSuggestionsPopup.IsOpen && SearchBox.Text.Length == 0:
                RefreshSuggestionsPopup();
                SearchSuggestionsPopup.IsOpen = true;
                e.Handled = true;
                break;
            case Key.Down when !IsAnyPreviewFocused:
                MoveSelection(+1);
                e.Handled = true;
                break;
            case Key.Up when !IsAnyPreviewFocused:
                MoveSelection(-1);
                e.Handled = true;
                break;
            case Key.PageDown when !IsAnyPreviewFocused:
                MoveSelection(+8);
                e.Handled = true;
                break;
            case Key.PageUp when !IsAnyPreviewFocused:
                MoveSelection(-8);
                e.Handled = true;
                break;
            case Key.Enter:
                if (HasPreviewSelection)
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
            case Key.L when mods == ModifierKeys.Control:
                CycleSplitRatio();
                e.Handled = true;
                break;
            case Key.OemComma when mods == ModifierKeys.Control:
                OpenSettings();
                e.Handled = true;
                break;
            case Key.M when mods == ModifierKeys.Control:
                ToggleWidgetMode();
                e.Handled = true;
                break;
            case Key.T when mods == (ModifierKeys.Control | ModifierKeys.Shift):
                ToggleTransparency();
                e.Handled = true;
                break;
            case Key.R when mods == ModifierKeys.Control:
                ToggleRevealSelected();
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
                if (HasPreviewSelection)
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
            case Key.Delete when mods == ModifierKeys.None && !IsAnyPreviewFocused && SearchBox.SelectionLength == 0 && SearchBox.CaretIndex == SearchBox.Text.Length:
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
        bool hasMarked = _items.Any(i => i.IsMarked);
        foreach (var item in _items)
        {
            item.MultiSelectActive = hasMarked;
        }
        UpdateContextualToolbar();
    }

    private void UpdateContextualToolbar()
    {
        if (IsCompact)
        {
            if (NormalToolbar is not null) NormalToolbar.Visibility = Visibility.Collapsed;
            if (MultiSelectToolbar is not null) MultiSelectToolbar.Visibility = Visibility.Collapsed;
            if (SensitiveToolbar is not null) SensitiveToolbar.Visibility = Visibility.Collapsed;
            return;
        }

        int markedCount = _items.Count(i => i.IsMarked);
        if (markedCount > 0)
        {
            if (NormalToolbar is not null) NormalToolbar.Visibility = Visibility.Collapsed;
            if (SensitiveToolbar is not null) SensitiveToolbar.Visibility = Visibility.Collapsed;
            if (MultiSelectToolbar is not null)
            {
                MultiSelectToolbar.Visibility = Visibility.Visible;
                if (MultiSelectCountBadge is not null)
                    MultiSelectCountBadge.Text = $"{markedCount} selected";
            }
            StatusText.Text = markedCount > 1 ? $"{markedCount} items selected — Enter merges, Ctrl+S stacks" : "1 item selected";
        }
        else if (ItemsList.SelectedItem is ItemViewModel cur && cur.IsSensitive && !cur.Revealed)
        {
            if (NormalToolbar is not null) NormalToolbar.Visibility = Visibility.Collapsed;
            if (MultiSelectToolbar is not null) MultiSelectToolbar.Visibility = Visibility.Collapsed;
            if (SensitiveToolbar is not null) SensitiveToolbar.Visibility = Visibility.Visible;
            StatusText.Text = "Sensitive item — press Ctrl+R to reveal";
        }
        else
        {
            if (MultiSelectToolbar is not null) MultiSelectToolbar.Visibility = Visibility.Collapsed;
            if (SensitiveToolbar is not null) SensitiveToolbar.Visibility = Visibility.Collapsed;
            if (NormalToolbar is not null) NormalToolbar.Visibility = Visibility.Visible;
            StatusText.Text = _svc.Settings.CaptureEnabled ? $"{_svc.Count()} items" : "Capture paused";
        }
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
        string effectiveQuery = SearchFilterChip.Combine(_activeChips, SearchBox.Text);
        if (!string.IsNullOrWhiteSpace(effectiveQuery)) _svc.AddRecentSearch(effectiveQuery);
        if (KeepOpen) ClearMarks(); else HidePalette();
        // Opened from the tray: there is no app to paste into, the item is just on the clipboard now.
        if (_paste.HasTarget) _paste.PasteIntoPreviousWindow();
    }

    private void CopySelected()
    {
        if (!WriteToClipboard()) return;
        string effectiveQuery = SearchFilterChip.Combine(_activeChips, SearchBox.Text);
        if (!string.IsNullOrWhiteSpace(effectiveQuery)) _svc.AddRecentSearch(effectiveQuery);
        if (KeepOpen) ClearMarks(); else HidePalette();
    }

    private void CopyPartialPreviewText()
    {
        var text = GetPreviewSelectedText();
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
        var text = GetPreviewSelectedText();
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
        var text = GetCurrentPreviewFullText();
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
        "Ctrl+L              tỷ lệ chia: 25/75, 30/70, 40/60, 50/50\n" +
        "Ctrl+,              open settings\n" +
        "Ctrl+M              toggle compact widget mode\n" +
        "Ctrl+Shift+T        toggle acrylic glass transparency\n" +
        "Ctrl+R              reveal a secret\n" +
        "Del                 delete item\n" +
        "Esc                 close\n\n" +
        "Search filters: type:sql  type:snippet  type:image  pinned:true  workspace:dev  after:2026-09-01";

    private void ShowHelpOverlay()
    {
        if (HelpOverlay is not null)
        {
            HelpOverlay.Visibility = Visibility.Visible;
            if (HelpSearchBox is not null)
            {
                HelpSearchBox.Text = "";
                HelpSearchBox.Focus();
            }
        }
    }

    private void HideHelpOverlay()
    {
        if (HelpOverlay is not null)
        {
            HelpOverlay.Visibility = Visibility.Collapsed;
            SearchBox.Focus();
        }
    }

    private void FilterHelpShortcuts(string? query)
    {
        if (HelpCategoriesPanel is null) return;
        string q = (query ?? "").Trim();
        bool filterEmpty = string.IsNullOrEmpty(q);

        foreach (UIElement catElem in HelpCategoriesPanel.Children)
        {
            if (catElem is Border catBorder && catBorder.Child is StackPanel sp)
            {
                int matchedRows = 0;
                foreach (UIElement rowElem in sp.Children)
                {
                    if (rowElem is DockPanel dp)
                    {
                        bool match = filterEmpty;
                        if (!match)
                        {
                            foreach (UIElement child in dp.Children)
                            {
                                if (child is TextBlock tb && tb.Text.Contains(q, StringComparison.OrdinalIgnoreCase))
                                {
                                    match = true;
                                    break;
                                }
                                if (child is Border b && b.Child is TextBlock btb && btb.Text.Contains(q, StringComparison.OrdinalIgnoreCase))
                                {
                                    match = true;
                                    break;
                                }
                            }
                        }
                        dp.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
                        if (match) matchedRows++;
                    }
                }
                catBorder.Visibility = (filterEmpty || matchedRows > 0) ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    /// <summary>F1: the keyboard reference, shown in the help modal or preview pane.</summary>
    private void ShowKeyHelp()
    {
        if (HelpOverlay is not null)
        {
            ShowHelpOverlay();
            return;
        }
        if (IsCompact)
        {
            StatusText.Text = Docked
                ? "Keys: Ctrl+K transform · Ctrl+, settings · Ctrl+S stack · Ctrl+1…9 · Ctrl+D undock"
                : "Keys: Ctrl+K transform · Ctrl+, settings · Ctrl+S stack · Ctrl+1…9 · Ctrl+T unpin";
            return;
        }
        SensitiveHiddenContainer.Visibility = Visibility.Collapsed;
        SensitiveRevealedBanner.Visibility = Visibility.Collapsed;
        SensitiveRevealedTextBanner.Visibility = Visibility.Collapsed;
        ImageContainer.Visibility = Visibility.Collapsed;
        UrlContainer.Visibility = Visibility.Collapsed;
        CodeContainer.Visibility = Visibility.Collapsed;

        PreviewHeaderBar.Visibility = Visibility.Visible;
        SetPreviewBadge("SHORTCUTS", "#F3F4F6", "#4B5563");
        PreviewStatsText.Text = "Keyboard reference (F1)";

        TextContainer.Visibility = Visibility.Visible;
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

    /// <summary>Raised by Stop button or Esc when a paste stack is active.</summary>
    public event Action? StopPasteStackRequested;

    public void UpdatePasteStackState(PasteStackState state)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (PasteStackBanner is null) return;
            if (state.IsActive && state.Total > 0)
            {
                PasteStackBanner.Visibility = Visibility.Visible;
                if (PasteStackStepText is not null)
                    PasteStackStepText.Text = $"{state.Position} / {state.Total}";
                if (PasteStackNextTitle is not null)
                    PasteStackNextTitle.Text = $"Next: {state.CurrentTitle ?? "item"}";
                if (PasteStackProgressBar is not null)
                {
                    PasteStackProgressBar.Maximum = state.Total;
                    PasteStackProgressBar.Value = state.Position;
                }
            }
            else
            {
                PasteStackBanner.Visibility = Visibility.Collapsed;
            }
        });
    }

    /// <summary>Raised by Ctrl+, or Settings button to open Settings window.</summary>
    public event Action? OpenSettingsRequested;

    private void OpenSettings()
    {
        if (!KeepOpen) HidePalette();
        OpenSettingsRequested?.Invoke();
    }

    private void ToggleTransparency()
    {
        _svc.Settings.EnableTransparency = !_svc.Settings.EnableTransparency;
        ApplyTransparency();
        SaveSettings();
    }

    internal void ApplyTransparency(bool? enableOverride = null, double? opacityOverride = null)
    {
        bool enable = enableOverride ?? _svc.Settings.EnableTransparency;
        double opacity = opacityOverride ?? _svc.Settings.TransparencyOpacity;
        if (TransparencyButton is not null) TransparencyButton.Tag = enable ? "active" : null;
        if (IsLoaded)
        {
            WindowBackdrop.Apply(this, enable);
        }
        byte alpha = enable ? (byte)(Math.Clamp(opacity, 0.5, 1.0) * 255) : (byte)255;
        if (RootBorder is not null)
        {
            RootBorder.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(alpha, 246, 248, 250));
            RootBorder.BorderBrush = enable
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x35, 0x00, 0x00, 0x00))
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xCD, 0xD1, 0xD8));
        }
        if (SearchContainer is not null)
        {
            SearchContainer.Background = enable
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x14, 0x00, 0x00, 0x00))
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF1, 0xF3, 0xF5));
        }
    }

    private void ToggleWidgetMode()
    {
        _svc.Settings.WidgetMode = !_svc.Settings.WidgetMode;
        SaveSettings();
        _resizingFromCode = true;
        try
        {
            ApplyLayout();
            Width = TargetWidth;
            Height = TargetHeight;
            PositionOnScreen();
            if (!IsCompact) UpdatePreview();
        }
        finally
        {
            _resizingFromCode = false;
        }
    }

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

    private void UpdateWorkspaceButtonText()
    {
        if (WorkspaceButtonText is null) return;
        var wsChip = _activeChips.FirstOrDefault(c => c.Key == "workspace");
        if (wsChip is not null)
        {
            WorkspaceButtonText.Text = $"{wsChip.Value} ▾";
        }
        else
        {
            var match = WorkspaceFilterRx.Match(SearchBox.Text);
            if (match.Success)
            {
                var wsName = match.Groups["name"].Value;
                WorkspaceButtonText.Text = string.IsNullOrEmpty(wsName) ? "All ▾" : $"{wsName} ▾";
            }
            else
            {
                WorkspaceButtonText.Text = "All ▾";
            }
        }
    }

    private void SetWorkspaceFilter(string? ws)
    {
        var existing = _activeChips.FirstOrDefault(c => c.Key == "workspace");
        if (existing is not null) _activeChips.Remove(existing);
        if (!string.IsNullOrWhiteSpace(ws))
        {
            _activeChips.Add(new SearchFilterChip("workspace", ws));
        }
        Reload();
    }

    private void ShowWorkspaceMenu(UIElement target)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = target,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
        };

        var wsChip = _activeChips.FirstOrDefault(c => c.Key == "workspace");
        string currentWs = wsChip is not null ? wsChip.Value : "";
        if (string.IsNullOrEmpty(currentWs))
        {
            var match = WorkspaceFilterRx.Match(SearchBox.Text);
            if (match.Success) currentWs = match.Groups["name"].Value;
        }

        var allItem = new MenuItem
        {
            Header = "Tất cả Workspaces / All Workspaces",
            IsChecked = string.IsNullOrEmpty(currentWs),
        };
        allItem.Click += (_, _) => SetWorkspaceFilter(null);
        menu.Items.Add(allItem);
        menu.Items.Add(new Separator());

        var workspaces = _svc.Workspaces().OrderBy(w => w.Name).ToList();
        if (workspaces.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = "(Chưa có workspace / No workspaces)", IsEnabled = false });
        }
        else
        {
            foreach (var ws in workspaces)
            {
                var item = new MenuItem
                {
                    Header = ws.Name,
                    IsChecked = string.Equals(ws.Name, currentWs, StringComparison.OrdinalIgnoreCase),
                };
                var wsName = ws.Name;
                item.Click += (_, _) => SetWorkspaceFilter(wsName);
                menu.Items.Add(item);
            }
        }

        menu.IsOpen = true;
    }

    /// <summary>Ctrl+W: all workspaces → first → second → … → all, by cycling the workspace filter chip.</summary>
    private void CycleWorkspaceFilter()
    {
        var names = _svc.Workspaces().Select(w => w.Name).ToList();
        var wsChip = _activeChips.FirstOrDefault(c => c.Key == "workspace");
        int index = wsChip is not null ? names.FindIndex(n => n.Equals(wsChip.Value, StringComparison.OrdinalIgnoreCase)) : -1;
        string? next = index + 1 < names.Count ? names[index + 1] : null;

        SetWorkspaceFilter(next);
    }

    // ==========================================
    // Filter Chips & Search Suggestions System
    // ==========================================

    private void UpdateSearchChipsState()
    {
        Placeholder.Visibility = (SearchBox.Text.Length == 0 && _activeChips.Count == 0) ? Visibility.Visible : Visibility.Collapsed;
        UpdatePillActiveStates();
    }

    private void OnSearchBoxTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingSearchBoxText) return;

        var text = SearchBox.Text;
        Placeholder.Visibility = (text.Length == 0 && _activeChips.Count == 0) ? Visibility.Visible : Visibility.Collapsed;
        UpdateWorkspaceButtonText();

        // 1. If text contains whitespace (e.g. "type:sql " or user pasted multiple tokens), extract completed filters
        if (text.Contains(' '))
        {
            var (extractedChips, remaining) = SearchFilterChip.ExtractFilters(text);
            if (extractedChips.Count > 0)
            {
                _isUpdatingSearchBoxText = true;
                try
                {
                    foreach (var chip in extractedChips)
                    {
                        AddOrReplaceChip(chip);
                    }
                    SearchBox.Text = remaining;
                    SearchBox.CaretIndex = SearchBox.Text.Length;
                }
                finally
                {
                    _isUpdatingSearchBoxText = false;
                }
                _debounce.Stop();
                Reload();
                return;
            }
        }
        // 2. Exact match of a recognized complete filter token (e.g. "type:sql", "pinned:true")
        else if (SearchFilterChip.TryParseFilter(text.Trim(), out var singleChip) && singleChip is not null)
        {
            if (IsRecognizedCompleteFilter(singleChip.Key, singleChip.Value))
            {
                _isUpdatingSearchBoxText = true;
                try
                {
                    AddOrReplaceChip(singleChip);
                    SearchBox.Text = "";
                    SearchBox.CaretIndex = 0;
                }
                finally
                {
                    _isUpdatingSearchBoxText = false;
                }
                _debounce.Stop();
                Reload();
                return;
            }
        }

        _debounce.Stop();
        _debounce.Start();
    }

    private bool IsRecognizedCompleteFilter(string key, string val)
    {
        var k = key.ToLowerInvariant();
        var v = val.ToLowerInvariant();

        if (k is "pinned" or "sensitive")
            return v is "true" or "false" or "yes" or "no" or "1" or "0";

        if (k is "before" or "after")
            return v.Length == 10 && DateTime.TryParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

        if (k is "type" or "is")
        {
            return v is "sql" or "json" or "xml" or "yaml" or "shell" or "markdown"
                or "code" or "url" or "link" or "links" or "image" or "files" or "file"
                or "text" or "snippet" or "template" or "sensitive" or "email" or "phone"
                or "number" or "ip" or "log";
        }

        if (k is "workspace" or "ws")
        {
            return _svc.Workspaces().Any(w => w.Name.Equals(val, StringComparison.OrdinalIgnoreCase));
        }

        return false;
    }

    private void AddOrReplaceChip(SearchFilterChip chip)
    {
        int existing = -1;
        for (int i = 0; i < _activeChips.Count; i++)
        {
            if (_activeChips[i].Key == chip.Key)
            {
                existing = i;
                break;
            }
        }
        if (existing >= 0) _activeChips[existing] = chip;
        else _activeChips.Add(chip);
    }

    private void OnRemoveFilterChipClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.Tag is SearchFilterChip chip)
        {
            _activeChips.Remove(chip);
            SearchBox.Focus();
            Reload();
        }
    }

    private void RefreshSuggestionsPopup()
    {
        PopulateRecentSearches();
        PopulateFilterPills();
    }

    private void PopulateRecentSearches()
    {
        var recent = _svc.Settings.RecentSearches;
        if (recent.Count == 0)
        {
            RecentSearchesSection.Visibility = Visibility.Collapsed;
        }
        else
        {
            RecentSearchesSection.Visibility = Visibility.Visible;
            RecentSearchesList.ItemsSource = recent.Take(6).ToList();
        }
    }

    private void OnClearAllRecentClick(object sender, RoutedEventArgs e)
    {
        _svc.Settings.RecentSearches.Clear();
        SaveSettings();
        RecentSearchesSection.Visibility = Visibility.Collapsed;
    }

    private void OnRemoveRecentSearchClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.Tag is string query)
        {
            _svc.RemoveRecentSearch(query);
            PopulateRecentSearches();
        }
    }

    private void OnRecentSearchItemClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.DataContext is string query)
        {
            ApplySearchQuery(query);
            SearchSuggestionsPopup.IsOpen = false;
            SearchBox.Focus();
        }
    }

    private void ApplySearchQuery(string query)
    {
        _activeChips.Clear();
        var (chips, rem) = SearchFilterChip.ExtractFilters(query);
        foreach (var c in chips) _activeChips.Add(c);
        _isUpdatingSearchBoxText = true;
        try
        {
            SearchBox.Text = rem;
            SearchBox.CaretIndex = rem.Length;
        }
        finally
        {
            _isUpdatingSearchBoxText = false;
        }
        Reload();
    }

    private void PopulateFilterPills()
    {
        // Type pills
        TypeFilterPills.Children.Clear();
        var types = new (string val, string label)[]
        {
            ("sql", "SQL"),
            ("code", "Code"),
            ("json", "JSON"),
            ("snippet", "Snippet"),
            ("image", "Images"),
            ("url", "URL"),
            ("files", "Files"),
            ("sensitive", "Sensitive"),
            ("text", "Text")
        };
        foreach (var t in types)
        {
            TypeFilterPills.Children.Add(CreateFilterPillButton("type", t.val, t.label));
        }

        // Workspace pills
        WorkspaceFilterPills.Children.Clear();
        foreach (var w in _svc.Workspaces())
        {
            WorkspaceFilterPills.Children.Add(CreateFilterPillButton("workspace", w.Name, w.Name));
        }

        // Pinned pills
        PinnedFilterPills.Children.Clear();
        PinnedFilterPills.Children.Add(CreateFilterPillButton("pinned", "true", "📌 Pinned"));
        PinnedFilterPills.Children.Add(CreateFilterPillButton("pinned", "false", "Unpinned"));

        // Date pills
        DateFilterPills.Children.Clear();
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        var last7 = DateTime.Today.AddDays(-7).ToString("yyyy-MM-dd");
        var last30 = DateTime.Today.AddDays(-30).ToString("yyyy-MM-dd");
        DateFilterPills.Children.Add(CreateFilterPillButton("after", today, "Today"));
        DateFilterPills.Children.Add(CreateFilterPillButton("after", last7, "Last 7 days"));
        DateFilterPills.Children.Add(CreateFilterPillButton("after", last30, "Last 30 days"));

        // Sensitive pills
        SensitiveFilterPills.Children.Clear();
        SensitiveFilterPills.Children.Add(CreateFilterPillButton("sensitive", "true", "🔒 Sensitive only"));
        SensitiveFilterPills.Children.Add(CreateFilterPillButton("sensitive", "false", "Non-sensitive"));

        UpdatePillActiveStates();
    }

    private Button CreateFilterPillButton(string key, string val, string label)
    {
        var btn = new Button
        {
            Content = label,
            DataContext = (key, val),
            Style = (Style)FindResource("PillButton"),
        };
        btn.Click += OnFilterPillClick;
        return btn;
    }

    private void UpdatePillActiveStates()
    {
        void CheckPanel(Panel panel)
        {
            if (panel is null) return;
            foreach (UIElement child in panel.Children)
            {
                if (child is Button b && b.DataContext is ValueTuple<string, string> tuple)
                {
                    bool isActive = _activeChips.Any(c => c.Key == tuple.Item1 && c.Value.Equals(tuple.Item2, StringComparison.OrdinalIgnoreCase));
                    b.Tag = isActive ? "active" : null;
                    b.ToolTip = isActive ? "Click to remove this filter" : $"Add filter {tuple.Item1}:{tuple.Item2}";
                }
            }
        }
        CheckPanel(TypeFilterPills);
        CheckPanel(WorkspaceFilterPills);
        CheckPanel(PinnedFilterPills);
        CheckPanel(DateFilterPills);
        CheckPanel(SensitiveFilterPills);
    }

    private void OnFilterPillClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is (string key, string val))
        {
            var existing = _activeChips.FirstOrDefault(c => c.Key == key && c.Value.Equals(val, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                _activeChips.Remove(existing);
            }
            else
            {
                AddOrReplaceChip(new SearchFilterChip(key, val));
            }
            SearchSuggestionsPopup.IsOpen = false;
            SearchBox.Focus();
            Reload();
        }
    }

    /// <summary>Ctrl+K / right-click: paste the selection (or the marked items) converted by a text transform.</summary>
    private void ShowTransformMenu()
    {
        if (ItemsList.SelectedItem is not ItemViewModel vm) return;

        var menu = new ContextMenu
        {
            PlacementTarget = ItemsList.ItemContainerGenerator.ContainerFromItem(vm) as UIElement ?? ItemsList,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
        };

        // 1. Primary actions
        var pasteItem = new MenuItem { Header = "Paste", InputGestureText = "Enter" };
        pasteItem.Click += (_, _) => PasteSelected();
        menu.Items.Add(pasteItem);

        var plainItem = new MenuItem { Header = "Paste as plain text", InputGestureText = "Ctrl+Shift+Enter" };
        plainItem.Click += (_, _) => PasteSelected(plainText: true);
        menu.Items.Add(plainItem);

        var copyItem = new MenuItem { Header = "Copy", InputGestureText = "Ctrl+C" };
        copyItem.Click += (_, _) => CopySelected();
        menu.Items.Add(copyItem);

        // 2. Transform Submenu (for text items)
        if (!vm.IsImage || _items.Any(i => i.IsMarked))
        {
            menu.Items.Add(new Separator());
            var transformSubmenu = new MenuItem { Header = "Transform →" };
            foreach (var t in TextTransforms.All)
            {
                if (t.Id is "json-pretty" or "sql" or "base64-encode") transformSubmenu.Items.Add(new Separator());
                var mi = new MenuItem { Header = t.Name };
                var curTransform = t;
                mi.Click += (_, _) => PasteSelected(transform: curTransform);
                transformSubmenu.Items.Add(mi);
            }
            menu.Items.Add(transformSubmenu);
        }

        menu.Items.Add(new Separator());

        // 3. Pin / Unpin
        var pinItem = new MenuItem
        {
            Header = vm.Item.IsPinned ? "Unpin item" : "Pin item",
            InputGestureText = "Ctrl+P",
        };
        pinItem.Click += (_, _) => TogglePinSelected();
        menu.Items.Add(pinItem);

        // 4. Move to Workspace Submenu
        var wsSubmenu = new MenuItem { Header = "Move to workspace →" };
        var workspaces = _svc.Workspaces().OrderBy(w => w.Name).ToList();
        if (workspaces.Count == 0)
        {
            wsSubmenu.Items.Add(new MenuItem { Header = "(No workspaces configured)", IsEnabled = false });
        }
        else
        {
            foreach (var ws in workspaces)
            {
                var wsItem = new MenuItem
                {
                    Header = ws.Name,
                    IsChecked = string.Equals(ws.Name, vm.Item.Workspace, StringComparison.OrdinalIgnoreCase),
                };
                var targetWs = ws.Name;
                wsItem.Click += (_, _) =>
                {
                    _svc.MoveToWorkspace(vm.Item, targetWs);
                    Reload(keepSelection: true);
                    StatusText.Text = $"Moved item to {targetWs}";
                };
                wsSubmenu.Items.Add(wsItem);
            }
        }
        menu.Items.Add(wsSubmenu);

        // 5. Snippet and Stack
        if (!vm.IsImage && !vm.IsSensitive)
        {
            var snippetItem = new MenuItem { Header = "Save as snippet", InputGestureText = "Ctrl+N" };
            snippetItem.Click += (_, _) => EditSnippet(createNew: true);
            menu.Items.Add(snippetItem);
        }

        var stackItem = new MenuItem { Header = "Start paste stack", InputGestureText = "Ctrl+S" };
        stackItem.Click += (_, _) =>
        {
            if (!vm.IsMarked)
            {
                vm.IsMarked = true;
                vm.MarkOrder = ++_markSequence;
                UpdateMarkedStatus();
            }
            StartPasteStack();
        };
        menu.Items.Add(stackItem);

        menu.Items.Add(new Separator());

        // 6. Delete
        var delItem = new MenuItem { Header = "Delete", InputGestureText = "Del" };
        delItem.Click += (_, _) => DeleteSelected();
        menu.Items.Add(delItem);

        menu.Closed += (_, _) => { if (IsVisible) SearchBox.Focus(); };
        menu.IsOpen = true;
    }

    private void ClearMarks()
    {
        foreach (var vm in _items)
        {
            vm.IsMarked = false;
            vm.MultiSelectActive = false;
        }
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
