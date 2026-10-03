using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using ClipboardManager.Core.Models;
using ClipboardManager.Core.Platform;

namespace ClipboardManager.Mac.Platform;

internal sealed class MacClipboardMonitor : IClipboardMonitor
{
    private DispatcherTimer? _timer;
    private string? _lastText;
    private bool _running;

    public event EventHandler<CapturedContent>? ContentCaptured;

    public void Start()
    {
        if (_running) return;
        _running = true;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _timer.Tick += async (_, _) => await CheckClipboardAsync();
        _timer.Start();
    }

    public void Stop()
    {
        _running = false;
        _timer?.Stop();
        _timer = null;
    }

    private async Task CheckClipboardAsync()
    {
        if (!_running) return;

        try
        {
            var cb = GetClipboard();
            if (cb is null) return;

            var text = await cb.GetTextAsync();
            if (!string.IsNullOrEmpty(text) && text != _lastText)
            {
                _lastText = text;
                ContentCaptured?.Invoke(this, CapturedContent.FromText(text));
            }
        }
        catch
        {
            // Ignore transient clipboard access errors
        }
    }

    private static IClipboard? GetClipboard()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow?.Clipboard;
        }
        return null;
    }

    public void Dispose()
    {
        Stop();
    }
}
