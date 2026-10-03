using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using ClipboardManager.Core.Models;
using ClipboardManager.Core.Platform;

namespace ClipboardManager.Mac.Platform;

internal sealed class MacClipboardWriter : IClipboardWriter
{
    private static IClipboard? GetClipboard()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow?.Clipboard;
        }
        return null;
    }

    public void Write(ClipboardPayload payload)
    {
        if (payload.Text is not null)
        {
            WriteText(payload.Text);
        }
    }

    public void WriteText(string text)
    {
        try
        {
            var cb = GetClipboard();
            cb?.SetTextAsync(text).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MacClipboardWriter] {ex.Message}");
        }
    }
}
