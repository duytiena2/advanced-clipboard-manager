using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using ClipboardManager.Core.Models;
using ClipboardManager.Core.Platform;
using ClipboardManager.Core.Services;
using ClipboardManager.Mac.Platform;

namespace ClipboardManager.Mac.Views;

public partial class MainWindow : Window
{
    private readonly ClipboardService? _svc;
    private readonly MacClipboardMonitor _monitor;
    private readonly MacClipboardWriter _writer;
    private readonly MacPasteSimulator _paste;
    private readonly IOcrEngine _ocrEngine;
    private readonly object _ocrGate = new();
    private Task _ocrQueue = Task.CompletedTask;

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
        _ocrEngine = new MacOcrEngine();

        _monitor = new MacClipboardMonitor();
        _monitor.ContentCaptured += (_, content) =>
        {
            if (_svc is null) return;
            var (outcome, item) = _svc.Capture(content);
            if (outcome == CaptureOutcome.Stored && item is { Kind: ContentKind.Image })
            {
                StartOcr(new[] { item });
            }
            Dispatcher.UIThread.Post(RefreshList);
        };
        _monitor.Start();

        SearchBox.KeyUp += OnSearchKeyUp;
        ItemsList.SelectionChanged += (_, _) => UpdatePreview();
        ItemsList.DoubleTapped += (_, _) => PasteSelected(plainTextOnly: false);
        KeyDown += OnWindowKeyDown;

        RefreshList();
        StartOcr(_svc.ImagesWithoutOcr(200));
    }

    private void StartOcr(IReadOnlyList<ClipboardItem> images)
    {
        if (_svc is null || !_svc.Settings.OcrEnabled || images.Count == 0 || !_ocrEngine.IsAvailable) return;
        var svc = _svc;
        var engine = _ocrEngine;
        lock (_ocrGate)
        {
            _ocrQueue = _ocrQueue.ContinueWith(async _ =>
            {
                try
                {
                    int found = await svc.RunOcrAsync(engine, images);
                    if (found > 0)
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            RefreshList();
                            UpdatePreview();
                        });
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[MacOcr] Error recognizing text: {ex.Message}");
                }
            }, TaskScheduler.Default).Unwrap();
        }
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
            if (item.Kind == ContentKind.Image)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"🖼 {item.Title} · {Math.Max(1, item.SizeBytes / 1024)} KB");
                sb.AppendLine();
                if (!string.IsNullOrWhiteSpace(item.OcrText))
                {
                    sb.AppendLine("--- Recognized Text (OCR) ---");
                    sb.AppendLine(item.OcrText);
                }
                else if (item.OcrText == "")
                {
                    sb.AppendLine("(No text detected in this image)");
                }
                else
                {
                    sb.AppendLine(_ocrEngine.IsAvailable
                        ? "(Extracting text in background...)"
                        : "(OCR unavailable)");
                }
                PreviewContent.Text = sb.ToString();
            }
            else
            {
                PreviewContent.Text = item.TextContent ?? "";
            }
        }
        else
        {
            PreviewTitle.Text = "";
            PreviewContent.Text = "";
        }
    }

    private void PasteSelected(bool plainTextOnly = false)
    {
        if (ItemsList.SelectedItem is ClipboardItem item)
        {
            var payload = _svc?.LoadPayload(item, plainText: plainTextOnly);
            if (payload is not null && !payload.IsEmpty)
            {
                _writer.Write(payload);
                Hide();
                _paste.PasteIntoPreviousWindow();
            }
            else if (!string.IsNullOrEmpty(item.TextContent))
            {
                _writer.WriteText(item.TextContent);
                Hide();
                _paste.PasteIntoPreviousWindow();
            }
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
            bool plainTextOnly = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            PasteSelected(plainTextOnly);
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
