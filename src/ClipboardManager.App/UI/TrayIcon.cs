using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using ClipboardManager.App.Native;
using WinForms = System.Windows.Forms;

namespace ClipboardManager.App.UI;

/// <summary>System-tray icon and menu (WinForms NotifyIcon, hosted inside the WPF app).</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;
    private readonly WinForms.ToolStripMenuItem _pauseItem;
    private readonly WinForms.ToolStripMenuItem _startupItem;
    private readonly Icon _appIcon;
    private readonly IntPtr _hIcon;
    private bool _settingStartup;

    public event Action? OpenRequested;
    public event Action<bool>? PauseToggled;
    public event Action? ClearRequested;
    public event Action<bool>? StartupToggled;
    public event Action? OpenDataFolderRequested;
    public event Action? OpenSettingsRequested;
    public event Action? ExitRequested;

    public TrayIcon(string hotkey, bool paused, bool startWithWindows)
    {
        (_appIcon, _hIcon) = DrawIcon();

        var menu = new WinForms.ContextMenuStrip();
        var open = new WinForms.ToolStripMenuItem($"Quick Paste\t{hotkey}") { Font = new Font(WinForms.Control.DefaultFont, FontStyle.Bold) };
        open.Click += (_, _) => OpenRequested?.Invoke();
        _pauseItem = new WinForms.ToolStripMenuItem("Pause clipboard capture") { CheckOnClick = true, Checked = paused };
        _pauseItem.CheckedChanged += (_, _) => { PauseToggled?.Invoke(_pauseItem.Checked); UpdateText(); };
        var clear = new WinForms.ToolStripMenuItem("Clear history (keeps pinned)");
        clear.Click += (_, _) => ClearRequested?.Invoke();
        _startupItem = new WinForms.ToolStripMenuItem("Start with Windows") { CheckOnClick = true, Checked = startWithWindows };
        _startupItem.CheckedChanged += (_, _) => { if (!_settingStartup) StartupToggled?.Invoke(_startupItem.Checked); };
        var folder = new WinForms.ToolStripMenuItem("Open data folder");
        folder.Click += (_, _) => OpenDataFolderRequested?.Invoke();
        var settings = new WinForms.ToolStripMenuItem("Edit settings (settings.json)");
        settings.Click += (_, _) => OpenSettingsRequested?.Invoke();
        var exit = new WinForms.ToolStripMenuItem("Exit");
        exit.Click += (_, _) => ExitRequested?.Invoke();

        menu.Items.AddRange(new WinForms.ToolStripItem[]
        {
            open, new WinForms.ToolStripSeparator(),
            _pauseItem, clear, new WinForms.ToolStripSeparator(),
            _startupItem, folder, settings, new WinForms.ToolStripSeparator(),
            exit,
        });

        _icon = new WinForms.NotifyIcon
        {
            Icon = _appIcon,
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) => { if (e.Button == WinForms.MouseButtons.Left) OpenRequested?.Invoke(); };
        UpdateText();
    }

    public void SetPaused(bool paused) => _pauseItem.Checked = paused;

    /// <summary>Reflects the real startup state without raising <see cref="StartupToggled"/> again.</summary>
    public void SetStartupChecked(bool enabled)
    {
        _settingStartup = true;
        try { _startupItem.Checked = enabled; }
        finally { _settingStartup = false; }
    }

    public void ShowBalloon(string title, string text, bool warning = false)
    {
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = text;
        _icon.BalloonTipIcon = warning ? WinForms.ToolTipIcon.Warning : WinForms.ToolTipIcon.Info;
        _icon.ShowBalloonTip(4000);
    }

    private void UpdateText() =>
        _icon.Text = _pauseItem.Checked ? "Clipboard Manager — capture paused" : "Clipboard Manager — recording";

    /// <summary>Draws a simple clipboard glyph so the app needs no binary .ico asset.</summary>
    private static (Icon, IntPtr) DrawIcon()
    {
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
        _icon.Visible = false;
        _icon.Dispose();
        _appIcon.Dispose();
        NativeMethods.DestroyIcon(_hIcon);
    }
}
