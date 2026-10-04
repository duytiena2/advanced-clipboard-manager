using System;
using System.Windows.Interop;
using ClipboardManager.App.Native;

namespace ClipboardManager.App.Platform;

/// <summary>Invisible message-only window used to receive WM_CLIPBOARDUPDATE and WM_HOTKEY.</summary>
internal sealed class MessageWindow : IDisposable
{
    private readonly HwndSource _source;

    public event Func<int, IntPtr, IntPtr, bool>? Message;

    public MessageWindow(string name)
    {
        var p = new HwndSourceParameters(name)
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
        };
        _source = new HwndSource(p);
        _source.AddHook(WndProc);
    }

    public IntPtr Handle => _source.Handle;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (Message is not null && Message(msg, wParam, lParam)) handled = true;
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
