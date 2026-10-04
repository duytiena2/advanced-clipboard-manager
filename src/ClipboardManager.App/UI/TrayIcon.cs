using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using ClipboardManager.App.Native;
using ClipboardManager.Core.Services;
using WinForms = System.Windows.Forms;

namespace ClipboardManager.App.UI;

/// <summary>System-tray icon and menu (WinForms NotifyIcon, hosted inside the WPF app).</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;
    private readonly WinForms.ToolStripMenuItem _pauseItem;
    private readonly WinForms.ToolStripMenuItem _startupItem;
    private readonly WinForms.ToolStripMenuItem _stopStackItem;
    private readonly WinForms.ToolStripMenuItem _openItem;
    private readonly WinForms.ToolStripMenuItem _clearItem;
    private readonly WinForms.ToolStripMenuItem _folderItem;
    private readonly WinForms.ToolStripMenuItem _settingsWindowItem;
    private readonly WinForms.ToolStripMenuItem _settingsJsonItem;
    private readonly WinForms.ToolStripMenuItem _checkUpdateItem;
    private readonly WinForms.ToolStripMenuItem _exitItem;
    private string _hotkey;
    private string? _stackStatus;
    private readonly Icon _appIcon;
    private readonly IntPtr _hIcon;
    private bool _settingStartup;
    private Action? _balloonAction;

    public event Action? OpenRequested;
    public event Action<bool>? PauseToggled;
    public event Action? ClearRequested;
    public event Action<bool>? StartupToggled;
    public event Action? OpenDataFolderRequested;
    public event Action? OpenSettingsRequested;
    public event Action? ExitRequested;
    public event Action? StopPasteStackRequested;
    public event Action? OpenSettingsWindowRequested;
    public event Action? CheckForUpdatesRequested;

    public TrayIcon(string hotkey, bool paused, bool startWithWindows)
    {
        _hotkey = hotkey;
        (_appIcon, _hIcon) = DrawIcon();

        var menu = new WinForms.ContextMenuStrip();
        _openItem = new WinForms.ToolStripMenuItem($"Quick Paste\t{hotkey}") { Font = new Font(WinForms.Control.DefaultFont, FontStyle.Bold) };
        _openItem.Click += (_, _) => OpenRequested?.Invoke();
        _stopStackItem = new WinForms.ToolStripMenuItem("Stop paste stack") { Visible = false };
        _stopStackItem.Click += (_, _) => StopPasteStackRequested?.Invoke();
        _pauseItem = new WinForms.ToolStripMenuItem("Pause clipboard capture") { CheckOnClick = true, Checked = paused };
        _pauseItem.CheckedChanged += (_, _) => { PauseToggled?.Invoke(_pauseItem.Checked); UpdateText(); };
        _clearItem = new WinForms.ToolStripMenuItem("Clear history (keeps pinned)");
        _clearItem.Click += (_, _) => ClearRequested?.Invoke();
        _startupItem = new WinForms.ToolStripMenuItem("Start with Windows") { CheckOnClick = true, Checked = startWithWindows };
        _startupItem.CheckedChanged += (_, _) => { if (!_settingStartup) StartupToggled?.Invoke(_startupItem.Checked); };
        _folderItem = new WinForms.ToolStripMenuItem("Open data folder");
        _folderItem.Click += (_, _) => OpenDataFolderRequested?.Invoke();
        _settingsWindowItem = new WinForms.ToolStripMenuItem("Settings…");
        _settingsWindowItem.Click += (_, _) => OpenSettingsWindowRequested?.Invoke();
        _settingsJsonItem = new WinForms.ToolStripMenuItem("Edit settings.json (advanced)");
        _settingsJsonItem.Click += (_, _) => OpenSettingsRequested?.Invoke();
        _checkUpdateItem = new WinForms.ToolStripMenuItem("Check for updates…");
        _checkUpdateItem.Click += (_, _) => CheckForUpdatesRequested?.Invoke();
        _exitItem = new WinForms.ToolStripMenuItem("Exit");
        _exitItem.Click += (_, _) => ExitRequested?.Invoke();

        menu.Items.AddRange(new WinForms.ToolStripItem[]
        {
            _openItem, _stopStackItem, new WinForms.ToolStripSeparator(),
            _pauseItem, _clearItem, new WinForms.ToolStripSeparator(),
            _settingsWindowItem, _checkUpdateItem, _startupItem, _folderItem, _settingsJsonItem, new WinForms.ToolStripSeparator(),
            _exitItem,
        });

        _icon = new WinForms.NotifyIcon
        {
            Icon = _appIcon,
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) => { if (e.Button == WinForms.MouseButtons.Left) OpenRequested?.Invoke(); };
        _icon.BalloonTipClicked += (_, _) => _balloonAction?.Invoke();

        ApplyLocalization();
        LocalizationService.LanguageChanged += ApplyLocalization;
    }

    public void ApplyLocalization()
    {
        _openItem.Text = $"{LocalizationService.Get("Tray_QuickPaste")}\t{_hotkey}";
        _stopStackItem.Text = LocalizationService.Get("Tray_StopPasteStack");
        _pauseItem.Text = LocalizationService.Get("Tray_PauseCapture");
        _clearItem.Text = LocalizationService.Get("Tray_ClearHistory");
        _startupItem.Text = LocalizationService.Get("Tray_StartWithWindows");
        _folderItem.Text = LocalizationService.Get("Tray_OpenDataFolder");
        _settingsWindowItem.Text = LocalizationService.Get("Tray_Settings");
        _settingsJsonItem.Text = LocalizationService.Get("Tray_EditSettingsJson");
        if (_checkUpdateItem.Tag is not "has_update")
            _checkUpdateItem.Text = LocalizationService.Get("Tray_CheckForUpdates");
        _exitItem.Text = LocalizationService.Get("Tray_Exit");
        UpdateText();
    }

    public void SetPaused(bool paused) => _pauseItem.Checked = paused;

    public void SetHotkey(string hotkey)
    {
        _hotkey = hotkey;
        _openItem.Text = $"{LocalizationService.Get("Tray_QuickPaste")}\t{_hotkey}";
    }

    /// <summary>Reflects the real startup state without raising <see cref="StartupToggled"/> again.</summary>
    public void SetStartupChecked(bool enabled)
    {
        _settingStartup = true;
        try { _startupItem.Checked = enabled; }
        finally { _settingStartup = false; }
    }

    public void ShowBalloon(string title, string text, bool warning = false, Action? onClick = null)
    {
        _balloonAction = onClick;
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = text;
        _icon.BalloonTipIcon = warning ? WinForms.ToolTipIcon.Warning : WinForms.ToolTipIcon.Info;
        _icon.ShowBalloonTip(4000);
    }

    public void SetUpdateAvailable(string tagName, Action onClick)
    {
        _checkUpdateItem.Text = $"⭐ Update available ({tagName})…";
        _checkUpdateItem.Font = new Font(WinForms.Control.DefaultFont, FontStyle.Bold);
        _checkUpdateItem.Click -= CheckUpdateClick;
        _checkUpdateItem.Click += (_, _) => onClick();
    }

    private void CheckUpdateClick(object? sender, EventArgs e) => CheckForUpdatesRequested?.Invoke();

    /// <summary>Shows a running paste stack in the tooltip and enables "Stop paste stack"; null hides both.</summary>
    public void SetPasteStackStatus(string? status)
    {
        _stackStatus = status;
        _stopStackItem.Visible = status is not null;
        UpdateText();
    }

    private void UpdateText()
    {
        var text = _stackStatus ?? (_pauseItem.Checked
            ? LocalizationService.Get("Tray_PausedTooltip", _hotkey)
            : LocalizationService.Get("Tray_RunningTooltip", _hotkey));
        _icon.Text = text.Length <= 127 ? text : text[..126] + "…"; // NotifyIcon.Text limit
    }

    /// <summary>Draws or extracts the clipboard icon for the system tray.</summary>
    private static (Icon, IntPtr) DrawIcon()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe) && System.IO.File.Exists(exe))
            {
                var assoc = Icon.ExtractAssociatedIcon(exe);
                if (assoc is not null)
                {
                    return (assoc, IntPtr.Zero);
                }
            }
        }
        catch { }

        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var bg = new SolidBrush(Color.FromArgb(0x0E, 0x6B, 0x68));
            using var path = RoundedRect(new Rectangle(2, 2, 28, 28), 7);
            g.FillPath(bg, path);
            using var pen = new Pen(Color.White, 2.4f) { LineJoin = LineJoin.Round };
            g.DrawRectangle(pen, 9, 9, 14, 16);
            using var white = new SolidBrush(Color.White);
            g.FillRectangle(white, 12, 6, 8, 5);
            g.FillRectangle(white, 12, 15, 8, 2);
            g.FillRectangle(white, 12, 19, 6, 2);
        }
        var h = bmp.GetHicon();
        return (Icon.FromHandle(h), h);
    }

    private static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        int d = radius * 2;
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public void Dispose()
    {
        LocalizationService.LanguageChanged -= ApplyLocalization;
        _icon.Visible = false;
        _icon.Dispose();
        _appIcon.Dispose();
        if (_hIcon != IntPtr.Zero)
        {
            NativeMethods.DestroyIcon(_hIcon);
        }
    }
}
