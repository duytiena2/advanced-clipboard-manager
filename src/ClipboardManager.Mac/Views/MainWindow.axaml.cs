using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Threading;
using ClipboardManager.Core.Models;
using ClipboardManager.Core.Platform;
using ClipboardManager.Core.Services;
using ClipboardManager.Mac.Platform;
using ClipboardManager.Mac.UI;

namespace ClipboardManager.Mac.Views;

public partial class MainWindow : Window
{
    private readonly ClipboardService? _svc;
    private readonly MacClipboardMonitor _monitor;
    private readonly MacClipboardWriter _writer;
    private readonly MacPasteSimulator _paste;
    private readonly MacHotkeyService _hotkeys;
    private readonly IOcrEngine _ocrEngine;
    private readonly IBarcodeScanner _barcodeScanner;
    private readonly MacPasteStackController _pasteStack;
    private readonly object _ocrGate = new();
    private Task _ocrQueue = Task.CompletedTask;
    private readonly DispatcherTimer _cleanupTimer;
    private readonly string _settingsPath;
    private SettingsWindow? _settingsWindow;

    private string? _pendingSecretHash;
    private DateTimeOffset? _pendingSecretExpiry;

    internal MacPasteStackController PasteStack => _pasteStack;

    public MainWindow()
    {
        InitializeComponent();

        var dataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "Application Support", "ClipboardManager");
        Directory.CreateDirectory(dataFolder);

        _settingsPath = Path.Combine(dataFolder, "settings.json");
        var settings = AppSettings.Load(_settingsPath);
        if (!File.Exists(_settingsPath)) settings.Save(_settingsPath);

        LocalizationService.SetLanguage(settings.Language);

        var protector = new MacDataProtector(dataFolder);
        _svc = new ClipboardService(dataFolder, settings, protector: protector);

        _writer = new MacClipboardWriter();
        _paste = new MacPasteSimulator();
        _ocrEngine = new MacOcrEngine();
        _barcodeScanner = new MacBarcodeScanner();
        _pasteStack = new MacPasteStackController(_svc, _writer);

        _monitor = new MacClipboardMonitor();
        _monitor.ContentCaptured += (_, content) =>
        {
            if (_svc is null) return;
            var (outcome, item) = _svc.Capture(content);
            if ((outcome == CaptureOutcome.Stored || (outcome == CaptureOutcome.Duplicate && string.IsNullOrEmpty(item?.OcrText)))
                && item is { Kind: ContentKind.Image })
            {
                StartOcr(new[] { item });
            }
            if (item is { IsSensitive: true } && outcome is CaptureOutcome.Stored or CaptureOutcome.Duplicate)
            {
                _pendingSecretHash = item.ContentHash;
                _pendingSecretExpiry = item.ExpiresAt;
            }
            Dispatcher.UIThread.Post(RefreshList);
        };
        _monitor.Start();

        _hotkeys = new MacHotkeyService();
        _hotkeys.Register(settings.QuickPasteHotkey, () => Dispatcher.UIThread.Post(ToggleWindow));

        _cleanupTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _cleanupTimer.Tick += (_, _) => RunCleanup();
        _cleanupTimer.Start();

        SearchBox.KeyUp += OnSearchKeyUp;
        ItemsList.SelectionChanged += (_, _) => UpdatePreview();
        ItemsList.DoubleTapped += (_, _) => PasteSelected(plainTextOnly: false);
        KeyDown += OnWindowKeyDown;

        RefreshList();
        StartOcr(_svc.ImagesWithoutOcr(200));

        Task.Run(async () => await MacUpdateChecker.CheckForUpdateAsync());
    }

    public void ShowSettings()
    {
        if (_svc is null) return;
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_svc, _settingsPath, () =>
        {
            _hotkeys.Register(_svc.Settings.QuickPasteHotkey, () => Dispatcher.UIThread.Post(ToggleWindow));
            RefreshList();
        });
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    public void ToggleWindow()
    {
        if (IsVisible)
        {
            Hide();
        }
        else
        {
            Show();
            Activate();
            SearchBox.Focus();
            SearchBox.SelectAll();
        }
    }

    public void ClearHistory()
    {
        _svc?.ClearHistory();
        RefreshList();
    }

    private void RunCleanup()
    {
        if (_svc is null) return;
        try
        {
            _svc.CleanupExpired();

            if (_pendingSecretExpiry.HasValue && DateTimeOffset.UtcNow >= _pendingSecretExpiry.Value)
            {
                _pendingSecretExpiry = null;
                _pendingSecretHash = null;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Cleanup] {ex.Message}");
        }
    }

    private void StartOcr(IReadOnlyList<ClipboardItem> images)
    {
        if (_svc is null || !_svc.Settings.OcrEnabled || images.Count == 0 || (!_ocrEngine.IsAvailable && !_barcodeScanner.IsAvailable)) return;
        var svc = _svc;
        var engine = _ocrEngine;
        var scanner = _barcodeScanner;
        lock (_ocrGate)
        {
            _ocrQueue = _ocrQueue.ContinueWith(async _ =>
            {
                try
                {
                    int found = await svc.RunOcrAsync(engine, images, scanner);
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
                    Console.WriteLine($"[MacOcr] Error recognizing text / QR: {ex.Message}");
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
        PreviewContent.Inlines?.Clear();

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
                    bool hasQr = item.OcrText.Contains("http://", StringComparison.OrdinalIgnoreCase) || item.OcrText.Contains("https://", StringComparison.OrdinalIgnoreCase);
                    sb.AppendLine(hasQr ? "--- Recognized Text / QR Code ---" : "--- Recognized Text (OCR) ---");
                    sb.AppendLine(item.OcrText);
                }
                else if (item.OcrText == "")
                {
                    sb.AppendLine("(No text or QR code detected in this image)");
                }
                else
                {
                    sb.AppendLine((_ocrEngine.IsAvailable || _barcodeScanner.IsAvailable)
                        ? "(Extracting text / QR in background...)"
                        : "(OCR / QR scanner unavailable)");
                }
                PreviewContent.Inlines = new InlineCollection { new Run(sb.ToString()) };
            }
            else
            {
                MacSyntaxHighlighter.Highlight(PreviewContent, item);
            }
        }
        else
        {
            PreviewTitle.Text = "";
            PreviewContent.Inlines?.Clear();
        }
    }

    private void PasteSelected(bool plainTextOnly = false)
    {
        // 1. Check if multiple items are selected: merge and paste!
        var selectedList = ItemsList.SelectedItems?.OfType<ClipboardItem>().ToList();
        if (selectedList is { Count: > 1 })
        {
            var merged = MergeService.Merge(selectedList, MergeSeparator.NewLine);
            _writer.WriteText(merged);
            Hide();
            _paste.PasteIntoPreviousWindow();
            return;
        }

        // 2. Single item paste
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

    private void TogglePinSelected()
    {
        if (_svc is null) return;
        var selected = ItemsList.SelectedItems?.OfType<ClipboardItem>().ToList();
        if (selected is { Count: > 0 })
        {
            foreach (var it in selected) _svc.TogglePin(it);
        }
        else if (ItemsList.SelectedItem is ClipboardItem single)
        {
            _svc.TogglePin(single);
        }
        RefreshList();
    }

    private void DeleteSelected()
    {
        if (_svc is null) return;
        var selected = ItemsList.SelectedItems?.OfType<ClipboardItem>().ToList();
        if (selected is { Count: > 0 })
        {
            foreach (var it in selected) _svc.Delete(it);
        }
        else if (ItemsList.SelectedItem is ClipboardItem single)
        {
            _svc.Delete(single);
        }
        RefreshList();
    }

    private void PasteIndex(int index, bool plainTextOnly)
    {
        if (ItemsList.ItemsSource is IReadOnlyList<ClipboardItem> list && index >= 0 && index < list.Count)
        {
            ItemsList.SelectedItem = list[index];
            PasteSelected(plainTextOnly);
        }
    }

    private void ShowTransformsMenu()
    {
        if (ItemsList.SelectedItem is not ClipboardItem item || string.IsNullOrEmpty(item.TextContent)) return;

        var menu = new ContextMenu();
        foreach (var t in TextTransforms.All)
        {
            var itemMenu = new MenuItem { Header = t.Name };
            itemMenu.Click += (_, _) =>
            {
                try
                {
                    var converted = t.Apply(item.TextContent);
                    _writer.WriteText(converted);
                    Hide();
                    _paste.PasteIntoPreviousWindow();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Transform] {ex.Message}");
                }
            };
            menu.Items.Add(itemMenu);
        }
        menu.Open(ItemsList);
    }

    private void OnSearchKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Up or Key.Down or Key.Enter) return;
        RefreshList();
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        bool hasCmd = e.KeyModifiers.HasFlag(KeyModifiers.Meta) || e.KeyModifiers.HasFlag(KeyModifiers.Control);

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
        else if (hasCmd && e.Key == Key.S)
        {
            var items = ItemsList.SelectedItems?.OfType<ClipboardItem>().ToList();
            if (items is { Count: > 0 })
            {
                _pasteStack.Start(items);
                Hide();
            }
            e.Handled = true;
        }
        else if (hasCmd && (e.Key == Key.OemComma || e.Key == Key.OemPeriod))
        {
            ShowSettings();
            e.Handled = true;
        }
        else if (hasCmd && e.Key == Key.P)
        {
            TogglePinSelected();
            e.Handled = true;
        }
        else if (hasCmd && e.Key == Key.K)
        {
            ShowTransformsMenu();
            e.Handled = true;
        }
        else if ((hasCmd && (e.Key == Key.Back || e.Key == Key.Delete)) || e.Key == Key.Delete)
        {
            DeleteSelected();
            e.Handled = true;
        }
        else if (hasCmd && e.Key >= Key.D1 && e.Key <= Key.D9)
        {
            int index = (int)e.Key - (int)Key.D1;
            PasteIndex(index, plainTextOnly: e.KeyModifiers.HasFlag(KeyModifiers.Shift));
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
        e.Cancel = true;
        Hide();
        base.OnClosing(e);
    }
}
