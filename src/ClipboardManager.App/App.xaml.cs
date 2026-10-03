using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using ClipboardManager.App.Platform;
using ClipboardManager.App.UI;
using ClipboardManager.Core.Models;
using ClipboardManager.Core.Services;
using WpfClipboard = System.Windows.Clipboard;

namespace ClipboardManager.App;

public partial class App : Application
{
    private Mutex? _mutex;
    private string _dataFolder = "";
    private string _settingsPath = "";
    private ClipboardService? _svc;
    private WindowsClipboardMonitor? _monitor;
    private WindowsHotkeyService? _hotkeys;
    private TrayIcon? _tray;
    private QuickPasteWindow? _palette;
    private DispatcherTimer? _cleanupTimer;
    private PasteStackController? _pasteStack;

    // Most recent sensitive capture: when it expires we also wipe it from the OS clipboard if it is still there.
    private string? _pendingSecretHash;
    private DateTimeOffset? _pendingSecretExpiry;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(initiallyOwned: true, @"Local\AdvancedClipboardManager.SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            MessageBox.Show("Advanced Clipboard Manager is already running. Look for its icon in the system tray.",
                "Clipboard Manager", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        _dataFolder = PackageInfo.DataFolder;
        _settingsPath = Path.Combine(_dataFolder, "settings.json");
        Directory.CreateDirectory(_dataFolder);

        DispatcherUnhandledException += (_, a) => { Log(a.Exception); a.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, a) => Log(a.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, a) => { Log(a.Exception); a.SetObserved(); };

        var settings = AppSettings.Load(_settingsPath);
        if (!File.Exists(_settingsPath)) settings.Save(_settingsPath);
        LocalizationService.SetLanguage(settings.Language);

        try
        {
            _svc = new ClipboardService(_dataFolder, settings, protector: new DpapiProtector());
        }
        catch (Exception ex)
        {
            Log(ex);
            MessageBox.Show("Could not open the clipboard database:\n\n" + ex.Message, "Clipboard Manager",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var writer = new WindowsClipboardWriter();
        var paste = new WindowsPasteSimulator();
        _palette = new QuickPasteWindow(_svc, writer, paste);

        _monitor = new WindowsClipboardMonitor();
        _monitor.ContentCaptured += (_, content) => Task.Run(() => OnCaptured(content));
        _monitor.Start();

        _tray = new TrayIcon(settings.QuickPasteHotkey, paused: !settings.CaptureEnabled, startWithWindows: SafeIsStartupEnabled());
        _tray.OpenRequested += () => _palette.ShowPalette();
        _tray.PauseToggled += paused =>
        {
            _svc.Settings.CaptureEnabled = !paused;
            _svc.Settings.Save(_settingsPath);
        };
        _tray.ClearRequested += () =>
        {
            var answer = MessageBox.Show("Delete all clipboard history except pinned items?", "Clear history",
                MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
            if (answer == MessageBoxResult.OK) _svc.ClearHistory();
        };
        _tray.StartupToggled += async enabled =>
        {
            try
            {
                bool actual = await StartupRegistration.SetEnabledAsync(enabled);
                if (actual != enabled)
                {
                    _tray.SetStartupChecked(actual);
                    _tray.ShowBalloon("Start with Windows",
                        "Windows blocked this. Turn the app on in Settings > Apps > Startup.", warning: true);
                }
            }
            catch (Exception ex)
            {
                Log(ex);
                _tray.SetStartupChecked(SafeIsStartupEnabled());
                _tray.ShowBalloon("Start with Windows", "Could not change the setting: " + ex.Message, warning: true);
            }
        };
        _pasteStack = new PasteStackController(_svc, writer);
        _pasteStack.StatusChanged += status => _tray.SetPasteStackStatus(status);
        _pasteStack.Notify += (message, warning) => _tray.ShowBalloon("Paste stack", message, warning);
        _pasteStack.StateChanged += state => _palette.UpdatePasteStackState(state);
        _palette.PasteStackRequested += items => _pasteStack.Start(items);
        _palette.StopPasteStackRequested += () => _pasteStack.Stop("Paste stack stopped.");
        _palette.OpenSettingsRequested += ShowSettings;
        _tray.StopPasteStackRequested += () => _pasteStack.Stop("Paste stack stopped.");
        // Raised on the UI thread for copies made in other apps (our own writes are skipped by the monitor).
        _monitor.ContentCaptured += (_, _) => _pasteStack.OnExternalClipboardChange();

        _tray.OpenDataFolderRequested += () => OpenShell(_dataFolder);
        _tray.OpenSettingsRequested += () => OpenShell(_settingsPath);
        _tray.OpenSettingsWindowRequested += ShowSettings;
        _tray.CheckForUpdatesRequested += () => RunUpdateCheck(silent: false);
        _tray.ExitRequested += () => Shutdown();

        _hotkeys = new WindowsHotkeyService();
        bool regOk = _hotkeys.Register(settings.QuickPasteHotkey, () =>
        {
            Log(new Exception($"HOTKEY FIRED! QuickPasteHotkey={settings.QuickPasteHotkey}"));
            _palette.TogglePalette();
        });
        Log(new Exception($"HOTKEY REGISTER: '{settings.QuickPasteHotkey}' Result={regOk}"));
        if (!regOk)
        {
            _tray.ShowBalloon("Shortcut unavailable",
                $"{settings.QuickPasteHotkey} is already used by another app. Pick another one in Settings (tray menu), " +
                "or click the tray icon to open Quick Paste.", warning: true);
        }
        else if (!StartupRegistration.LaunchedAtStartup(e.Args))
        {
            _tray.ShowBalloon("Clipboard Manager is running", $"Press {settings.QuickPasteHotkey} to open Quick Paste.");
            _palette.ShowPalette();
        }

        if (!_svc.FullTextSearchEnabled) Log(new InvalidOperationException("FTS5 not available in the loaded SQLite library; using LIKE search."));

        _cleanupTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _cleanupTimer.Tick += (_, _) => RunCleanup();
        _cleanupTimer.Start();
        RunCleanup();

        // Images copied before OCR was available (or while it was off) get their text now.
        StartOcr(_svc.ImagesWithoutOcr(200));

        // Background update check for non-packaged builds (MSIX is updated by Microsoft Store)
        if (!PackageInfo.IsPackaged)
        {
            Task.Delay(TimeSpan.FromSeconds(5)).ContinueWith(_ => Dispatcher.Invoke(() => RunUpdateCheck(silent: true)));
        }
    }

    private void OnCaptured(CapturedContent content)
    {
        if (_svc is null) return;
        try
        {
            var (outcome, item) = _svc.Capture(content);
            if (outcome == CaptureOutcome.Stored && item is { Kind: ContentKind.Image }) StartOcr(new[] { item });
            if (item is { IsSensitive: true } && (outcome is CaptureOutcome.Stored or CaptureOutcome.Duplicate))
            {
                _pendingSecretHash = item.ContentHash;
                _pendingSecretExpiry = item.ExpiresAt;
            }
        }
        catch (Exception ex)
        {
            Log(ex);
        }
    }

    private SettingsWindow? _settingsWindow;

    private void ShowSettings()
    {
        if (_svc is null) return;
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }
        _ocr ??= new WindowsOcrEngine();
        _settingsWindow = new SettingsWindow(_svc, _settingsPath, _ocr.Language);
        _settingsWindow.Saved += OnSettingsSaved;
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    /// <summary>Applies what can't just be read from the shared settings object: the global shortcut and tray state.</summary>
    private void OnSettingsSaved(string oldHotkey)
    {
        if (_svc is null || _tray is null || _palette is null) return;
        var s = _svc.Settings;
        _palette.ApplyTransparency(s.EnableTransparency, s.TransparencyOpacity);
        _palette.ApplyLocalization();
        _tray.ApplyLocalization();
        _tray.SetPaused(!s.CaptureEnabled);
        if (!string.Equals(oldHotkey, s.QuickPasteHotkey, StringComparison.OrdinalIgnoreCase))
        {
            _hotkeys?.Dispose();
            _hotkeys = new WindowsHotkeyService();
            if (_hotkeys.Register(s.QuickPasteHotkey, () => _palette.TogglePalette()))
            {
                _tray.SetHotkey(s.QuickPasteHotkey);
                _tray.ShowBalloon("Shortcut changed", $"Press {s.QuickPasteHotkey} to open Quick Paste.");
            }
            else
            {
                // Keep the old one working rather than leaving the user with no shortcut.
                _hotkeys.Register(oldHotkey, () => _palette.TogglePalette());
                s.QuickPasteHotkey = oldHotkey;
                try { s.Save(_settingsPath); } catch (IOException) { }
                _tray.ShowBalloon("Shortcut unavailable", $"Another app already uses that shortcut; {oldHotkey} still works.", warning: true);
            }
        }
        StartOcr(_svc.ImagesWithoutOcr(200)); // in case OCR was just turned on
    }

    /// <summary>Recognizes text in images in the background (one batch at a time) so they become searchable.</summary>
    private void StartOcr(System.Collections.Generic.IReadOnlyList<ClipboardItem> images)
    {
        if (_svc is null || !_svc.Settings.OcrEnabled || images.Count == 0) return;
        var svc = _svc;
        lock (_ocrGate) // called from capture threads and the UI thread
        {
            _ocr ??= new WindowsOcrEngine();
            if (!_ocr.IsAvailable) return;
            var engine = _ocr;
            _ocrQueue = _ocrQueue.ContinueWith(async _ =>
            {
                try { await svc.RunOcrAsync(engine, images); }
                catch (Exception ex) { Log(ex); }
            }, TaskScheduler.Default).Unwrap();
        }
    }

    private readonly object _ocrGate = new();
    private WindowsOcrEngine? _ocr;
    private Task _ocrQueue = Task.CompletedTask;

    private void RunCleanup()
    {
        if (_svc is null) return;
        try
        {
            _svc.CleanupExpired();
            WipeExpiredSecretFromClipboard();
        }
        catch (Exception ex)
        {
            Log(ex);
        }
    }

    private void WipeExpiredSecretFromClipboard()
    {
        if (_pendingSecretHash is null || _pendingSecretExpiry is null) return;
        if (DateTimeOffset.UtcNow < _pendingSecretExpiry.Value) return;
        var hash = _pendingSecretHash;
        _pendingSecretHash = null;
        _pendingSecretExpiry = null;
        try
        {
            if (!WpfClipboard.ContainsText()) return;
            var current = WpfClipboard.GetText();
            if (_svc?.HashText(current) == hash) WpfClipboard.Clear();
        }
        catch (COMException) { /* clipboard busy; skip this round */ }
    }

    private static bool SafeIsStartupEnabled()
    {
        try { return StartupRegistration.IsEnabled(); }
        catch (Exception) { return false; }
    }

    private static void OpenShell(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { Log(ex); }
    }

    private async void RunUpdateCheck(bool silent)
    {
        if (_tray is null) return;
        try
        {
            if (!silent) _tray.ShowBalloon("Clipboard Manager", "Checking for updates…");
            var update = await UpdateChecker.CheckForUpdateAsync().ConfigureAwait(true);
            if (update is not null)
            {
                _tray.SetUpdateAvailable(update.TagName, () => ApplyUpdate(update));
                _tray.ShowBalloon("Update available",
                    $"Version {update.TagName} is available! Click to update now.",
                    onClick: () => ApplyUpdate(update));
            }
            else if (!silent)
            {
                _tray.ShowBalloon("Clipboard Manager",
                    $"You are up to date! Version {UpdateChecker.CurrentVersionString} is the latest version.");
            }
        }
        catch (Exception ex)
        {
            Log(ex);
            if (!silent)
            {
                _tray.ShowBalloon("Check for updates", "Could not check for updates. Please try again later.", warning: true);
            }
        }
    }

    private async void ApplyUpdate(UpdateInfo update)
    {
        if (_tray is null) return;

        if (string.IsNullOrWhiteSpace(update.DownloadUrl))
        {
            OpenShell(update.ReleaseUrl);
            return;
        }

        var result = MessageBox.Show(
            $"A new version of Advanced Clipboard Manager ({update.TagName}) is available.\n\n" +
            "Would you like to download and install it now?\nThe app will restart automatically after updating.",
            "Update Available",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);

        if (result != MessageBoxResult.Yes) return;

        _tray.ShowBalloon("Updating Clipboard Manager", $"Downloading version {update.TagName} in the background…");

        var installerPath = await Task.Run(() => UpdateChecker.DownloadInstallerAsync(update));
        if (string.IsNullOrEmpty(installerPath) || !File.Exists(installerPath))
        {
            _tray.ShowBalloon("Update failed", "Could not download the update. Opening release page in browser…", warning: true);
            OpenShell(update.ReleaseUrl);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = "/SILENT",
                UseShellExecute = true
            });

            Dispatcher.Invoke(Shutdown);
        }
        catch (Exception ex)
        {
            Log(ex);
            try { Process.Start(new ProcessStartInfo(installerPath) { UseShellExecute = true }); } catch { }
        }
    }

    private static readonly object LogGate = new();

    internal static void Log(Exception? ex)
    {
        if (ex is null) return;
        try
        {
            var folder = PackageInfo.DataFolder;
            Directory.CreateDirectory(folder);
            lock (LogGate)
            {
                File.AppendAllText(Path.Combine(folder, "error.log"), $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
            }
        }
        catch (IOException) { }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _cleanupTimer?.Stop();
        _pasteStack?.Dispose();
        _hotkeys?.Dispose();
        _monitor?.Dispose();
        _tray?.Dispose();
        _palette?.Close();
        _svc?.Dispose();
        try { _mutex?.ReleaseMutex(); } catch (ApplicationException) { }
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
