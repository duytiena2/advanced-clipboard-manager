using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
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
    private bool _hiding;

    internal QuickPasteWindow(ClipboardService svc, IClipboardWriter writer, WindowsPasteSimulator paste)
    {
        _svc = svc;
        _writer = writer;
        _paste = paste;
        InitializeComponent();

        ItemsList.ItemsSource = _items;
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
        IsVisibleChanged += (_, _) => { if (IsVisible) _tick.Start(); else _tick.Stop(); };

        SearchBox.TextChanged += (_, _) =>
        {
            Placeholder.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            _debounce.Stop();
            _debounce.Start();
        };
        ItemsList.SelectionChanged += (_, _) => UpdatePreview();
        ItemsList.MouseDoubleClick += (_, _) => PasteSelected();
        PreviewKeyDown += OnPreviewKeyDown;
        Deactivated += (_, _) => HidePalette();

        // Borderless window: drag it by the search bar or the footer (the text box itself keeps normal mouse behavior).
        Header.MouseLeftButtonDown += OnDragAreaMouseDown;
        Footer.MouseLeftButtonDown += OnDragAreaMouseDown;

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

    public void ShowPalette()
    {
        _paste.RememberForegroundWindow();
        PositionOnScreen();
        SearchBox.Text = "";
        Reload();
        _hiding = false;
        Show();
        Activate();
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
    }

    public void HidePalette()
    {
        if (_hiding || !IsVisible) return;
        _hiding = true;
        foreach (var vm in _items) vm.IsMarked = false;
        Hide();
        _hiding = false;
    }

    private void OnDragAreaMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
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
        try { _svc.Settings.Save(Path.Combine(_svc.DataFolder, "settings.json")); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        SearchBox.Focus();
    }

    private void PositionOnScreen()
    {
        var s = _svc.Settings;
        if (s.QuickPasteLeft is double left && s.QuickPasteTop is double top && IsOnScreen(left, top))
        {
            Left = left;
            Top = top;
            return;
        }
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + Math.Max(40, (area.Height - Height) * 0.3);
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
        var markedIds = _items.Where(i => i.IsMarked).Select(i => i.Item.Id).ToHashSet();

        var results = _svc.Search(SearchBox.Text, MaxResults);
        _items.Clear();
        foreach (var item in results)
        {
            _items.Add(new ItemViewModel(item, _svc.DataFolder) { IsMarked = markedIds.Contains(item.Id) });
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
            case Key.Down:
                MoveSelection(+1);
                e.Handled = true;
                break;
            case Key.Up:
                MoveSelection(-1);
                e.Handled = true;
                break;
            case Key.PageDown:
                MoveSelection(+8);
                e.Handled = true;
                break;
            case Key.PageUp:
                MoveSelection(-8);
                e.Handled = true;
                break;
            case Key.Enter:
                PasteSelected();
                e.Handled = true;
                break;
            case Key.P when mods == ModifierKeys.Control:
                TogglePinSelected();
                e.Handled = true;
                break;
            case Key.R when mods == ModifierKeys.Control:
                if (ItemsList.SelectedItem is ItemViewModel r) { r.Revealed = !r.Revealed; UpdatePreview(); }
                e.Handled = true;
                break;
            case Key.Space when mods == ModifierKeys.Control:
                if (ItemsList.SelectedItem is ItemViewModel m) { m.IsMarked = !m.IsMarked; MoveSelection(+1); UpdateMarkedStatus(); }
                e.Handled = true;
                break;
            case Key.C when mods == ModifierKeys.Control && SearchBox.SelectionLength == 0:
                CopySelected();
                e.Handled = true;
                break;
            case Key.Delete when mods == ModifierKeys.None && SearchBox.SelectionLength == 0 && SearchBox.CaretIndex == SearchBox.Text.Length:
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

    private bool WriteToClipboard()
    {
        try
        {
            var marked = _items.Where(i => i.IsMarked).ToList();
            if (marked.Count > 1)
            {
                _writer.WriteText(MergeService.Merge(marked.Select(m => m.Item), MergeSeparator.NewLine));
                foreach (var m in marked) _svc.MarkUsed(m.Item);
                return true;
            }
            var vm = marked.Count == 1 ? marked[0] : ItemsList.SelectedItem as ItemViewModel;
            if (vm is null) return false;
            _writer.Write(vm.Item, _svc.DataFolder);
            _svc.MarkUsed(vm.Item);
            return true;
        }
        catch (COMException)
        {
            StatusText.Text = "Clipboard is busy — try again";
            return false;
        }
    }

    private void PasteSelected()
    {
        if (!WriteToClipboard()) return;
        HidePalette();
        // Opened from the tray: there is no app to paste into, the item is just on the clipboard now.
        if (_paste.HasTarget) _paste.PasteIntoPreviousWindow();
    }

    private void CopySelected()
    {
        if (WriteToClipboard()) HidePalette();
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
