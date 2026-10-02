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
        _tray.OpenDataFolderRequested += () => OpenShell(_dataFolder);
        _tray.OpenSettingsRequested += () => OpenShell(_settingsPath);
        _tray.ExitRequested += () => Shutdown();

        _hotkeys = new WindowsHotkeyService();
        if (!_hotkeys.Register(settings.QuickPasteHotkey, () => _palette.TogglePalette()))
        {
            _tray.ShowBalloon("Shortcut unavailable",
                $"{settings.QuickPasteHotkey} is already used by another app. Change \"QuickPasteHotkey\" in settings.json, " +
                "or click the tray icon to open Quick Paste.", warning: true);
        }
        else if (!StartupRegistration.LaunchedAtStartup(e.Args))
        {
            _tray.ShowBalloon("Clipboard Manager is running", $"Press {settings.QuickPasteHotkey} to open Quick Paste.");
        }

        if (!_svc.FullTextSearchEnabled) Log(new InvalidOperationException("FTS5 not available in the loaded SQLite library; using LIKE search."));

        _cleanupTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _cleanupTimer.Tick += (_, _) => RunCleanup();
        _cleanupTimer.Start();
        RunCleanup();
    }

    private void OnCaptured(CapturedContent content)
    {
        if (_svc is null) return;
        try
        {
            var (outcome, item) = _svc.Capture(content);
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
