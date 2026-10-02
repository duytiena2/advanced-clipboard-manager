using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace ClipboardManager.App.Platform;

public enum DockEdge
{
    None,
    Left,
    Right,
}

/// <summary>
/// Docks a WPF window to a screen edge as a Windows "application desktop toolbar" (like the taskbar): the shell reserves
/// the space, so maximized windows don't cover it. Uses the full height of the window's monitor.
/// </summary>
internal sealed class AppBar : IDisposable
{
    private const int ABM_NEW = 0x0;
    private const int ABM_REMOVE = 0x1;
    private const int ABM_QUERYPOS = 0x2;
    private const int ABM_SETPOS = 0x3;
    private const int ABM_ACTIVATE = 0x6;
    private const int ABM_WINDOWPOSCHANGED = 0x9;
    private const int ABN_POSCHANGED = 0x1;
    private const int ABE_LEFT = 0;
    private const int ABE_RIGHT = 2;
    private const int WM_ACTIVATE = 0x0006;
    private const int WM_WINDOWPOSCHANGED = 0x0047;
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    private readonly Window _window;
    private readonly double _widthDip;
    private readonly int _callbackMessage = RegisterWindowMessage("AdvancedClipboardManager.AppBar");
    private HwndSource? _source;
    private bool _registered;

    public DockEdge Edge { get; private set; } = DockEdge.None;

    public AppBar(Window window, double widthDip)
    {
        _window = window;
        _widthDip = widthDip;
    }

    public void Dock(DockEdge edge)
    {
        if (edge == DockEdge.None) { Undock(); return; }
        var hwnd = new WindowInteropHelper(_window).EnsureHandle();
        if (_source is null)
        {
            _source = HwndSource.FromHwnd(hwnd);
            _source.AddHook(WndProc);
        }
        if (!_registered)
        {
            var abd = NewData(hwnd);
            abd.uCallbackMessage = _callbackMessage;
            if (SHAppBarMessage(ABM_NEW, ref abd) == UIntPtr.Zero) return;
            _registered = true;
        }
        Edge = edge;
        Reposition(hwnd);
    }

    public void Undock()
    {
        Edge = DockEdge.None;
        if (!_registered) return;
        var abd = NewData(new WindowInteropHelper(_window).Handle);
        SHAppBarMessage(ABM_REMOVE, ref abd);
        _registered = false;
    }

    private void Reposition(IntPtr hwnd)
    {
        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info)) return;

        int width = (int)Math.Round(_widthDip * VisualTreeHelper.GetDpi(_window).DpiScaleX);
        var abd = NewData(hwnd);
        abd.uEdge = Edge == DockEdge.Left ? ABE_LEFT : ABE_RIGHT;
        abd.rc = info.rcMonitor;
        if (Edge == DockEdge.Left) abd.rc.right = abd.rc.left + width; else abd.rc.left = abd.rc.right - width;

        // The shell may shrink the rectangle (taskbar, other app bars); keep our width on the chosen edge.
        SHAppBarMessage(ABM_QUERYPOS, ref abd);
        if (Edge == DockEdge.Left) abd.rc.right = abd.rc.left + width; else abd.rc.left = abd.rc.right - width;
        SHAppBarMessage(ABM_SETPOS, ref abd);

        SetWindowPos(hwnd, IntPtr.Zero, abd.rc.left, abd.rc.top, abd.rc.right - abd.rc.left, abd.rc.bottom - abd.rc.top,
            SWP_NOZORDER | SWP_NOACTIVATE);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (!_registered) return IntPtr.Zero;
        if (msg == _callbackMessage && wParam.ToInt32() == ABN_POSCHANGED)
        {
            Reposition(hwnd); // taskbar moved, resolution changed, another app bar appeared…
            handled = true;
        }
        else if (msg == WM_ACTIVATE)
        {
            var abd = NewData(hwnd);
            SHAppBarMessage(ABM_ACTIVATE, ref abd);
        }
        else if (msg == WM_WINDOWPOSCHANGED)
        {
            var abd = NewData(hwnd);
            SHAppBarMessage(ABM_WINDOWPOSCHANGED, ref abd);
        }
        return IntPtr.Zero;
    }

    private static APPBARDATA NewData(IntPtr hwnd) => new() { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = hwnd };

    public void Dispose()
    {
        Undock();
        _source?.RemoveHook(WndProc);
        _source = null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left, top, right, bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uCallbackMessage;
        public int uEdge;
        public RECT rc;
        public IntPtr lParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("shell32.dll")]
    private static extern UIntPtr SHAppBarMessage(int dwMessage, ref APPBARDATA pData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegisterWindowMessage(string lpString);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
}
