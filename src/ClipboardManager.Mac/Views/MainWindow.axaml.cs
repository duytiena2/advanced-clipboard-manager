using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using ClipboardManager.Core.Models;
using ClipboardManager.Core.Services;
using ClipboardManager.Mac.Platform;

namespace ClipboardManager.Mac.Views;

public partial class MainWindow : Window
{
    private readonly ClipboardService? _svc;
    private readonly MacClipboardMonitor _monitor;
    private readonly MacClipboardWriter _writer;
    private readonly MacPasteSimulator _paste;

    public MainWindow()
    {
        InitializeComponent();

        var dataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "Application Support", "ClipboardManager");
        Directory.CreateDirectory(dataFolder);

        var settingsPath = Path.Combine(dataFolder, "settings.json");
        var settings = AppSettings.Load(settingsPath);
        _svc = new ClipboardService(dataFolder, settings);

        _writer = new MacClipboardWriter();
        _paste = new MacPasteSimulator();

        _monitor = new MacClipboardMonitor();
        _monitor.ContentCaptured += (_, content) =>
        {
            _svc?.Capture(content);
            Dispatcher.UIThread.Post(RefreshList);
        };
        _monitor.Start();

        SearchBox.KeyUp += OnSearchKeyUp;
        ItemsList.SelectionChanged += (_, _) => UpdatePreview();
        ItemsList.DoubleTapped += (_, _) => PasteSelected();
        KeyDown += OnWindowKeyDown;

        RefreshList();
    }

    private void RefreshList()
    {
        if (_svc is null) return;
        var query = SearchBox.Text?.Trim();
        var results = _svc.Search(string.IsNullOrEmpty(query) ? null : query);
        ItemsList.ItemsSource = results;
        ItemCountText.Text = $"{results.Count} items";
        if (results.Count > 0 && ItemsList.SelectedIndex < 0)
        {
            ItemsList.SelectedIndex = 0;
        }
    }

    private void UpdatePreview()
    {
        if (ItemsList.SelectedItem is ClipboardItem item)
        {
            PreviewTitle.Text = item.Title;
            PreviewContent.Text = item.TextContent ?? "(Image/Binary item)";
        }
        else
        {
            PreviewTitle.Text = "";
            PreviewContent.Text = "";
        }
    }

    private void PasteSelected()
    {
        if (ItemsList.SelectedItem is ClipboardItem item && !string.IsNullOrEmpty(item.TextContent))
        {
            _writer.WriteText(item.TextContent);
            Hide();
            _paste.PasteIntoPreviousWindow();
        }
    }

    private void OnSearchKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Up or Key.Down or Key.Enter) return;
        RefreshList();
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Hide();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            PasteSelected();
            e.Handled = true;
        }
        else if (e.Key == Key.Down && SearchBox.IsFocused)
        {
            if (ItemsList.ItemCount > 0)
            {
                ItemsList.SelectedIndex = Math.Min(ItemsList.SelectedIndex + 1, ItemsList.ItemCount - 1);
                ItemsList.Focus();
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Up && ItemsList.IsFocused && ItemsList.SelectedIndex == 0)
        {
            SearchBox.Focus();
            e.Handled = true;
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Hide instead of exit when user closes palette window
        e.Cancel = true;
        Hide();
        base.OnClosing(e);
    }
}
