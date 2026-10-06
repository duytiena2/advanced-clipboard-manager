using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using ClipboardManager.App.Native;
using ClipboardManager.Core.Platform;

namespace ClipboardManager.App.Platform;

/// <summary>Returns focus to the window that was active before Quick Paste opened and sends Ctrl+V to it.</summary>
internal sealed class WindowsPasteSimulator : IPasteSimulator
{
    private IntPtr _target;

    /// <summary>Call right before showing the palette.</summary>
    /// <param name="onlyIfPasteTarget">
    /// true = keep the previous target when the current foreground window is not a valid one (our own palette, the taskbar…).
    /// Used by "keep open" mode, which polls this to follow the app the user is working in.
    /// </param>
    public void RememberForegroundWindow(bool onlyIfPasteTarget = false)
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (IsPasteTarget(hwnd)) _target = hwnd;
        else if (!onlyIfPasteTarget) _target = IntPtr.Zero;
    }

    /// <summary>
    /// Opening the palette from the tray makes the taskbar the foreground window; pasting there would do nothing useful,
    /// so in that case the item is only copied to the clipboard.
    /// </summary>
    private static bool IsPasteTarget(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == (uint)Environment.ProcessId) return false;
        var cls = new StringBuilder(256);
        NativeMethods.GetClassName(hwnd, cls, cls.Capacity);
        return cls.ToString() is not ("Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "NotifyIconOverflowWindow"
            or "TopLevelWindowForOverflowXamlIsland" or "Progman" or "WorkerW");
    }

    public bool HasTarget => _target != IntPtr.Zero;

    public string GetTargetWindowTitle()
    {
        if (_target == IntPtr.Zero || !NativeMethods.IsWindow(_target)) return "";
        var sb = new StringBuilder(256);
        NativeMethods.GetWindowText(_target, sb, sb.Capacity);
        return sb.ToString();
    }

    public bool IsRemoteDesktopTarget
    {
        get
        {
            var title = GetTargetWindowTitle();
            return title.Contains("UltraViewer", StringComparison.OrdinalIgnoreCase)
                || title.Contains("TeamViewer", StringComparison.OrdinalIgnoreCase)
                || title.Contains("AnyDesk", StringComparison.OrdinalIgnoreCase);
        }
    }

    public void PasteIntoPreviousWindow() => _ = PasteAsync();

    private async Task PasteAsync()
    {
        if (_target == IntPtr.Zero || !NativeMethods.IsWindow(_target)) return;
        NativeMethods.SetForegroundWindow(_target);
        await Task.Delay(60); // let the target window take focus

        // Wait (briefly) until the user has released modifier keys, otherwise Ctrl+V becomes Ctrl+Shift+V etc.
        for (int i = 0; i < 25 && ModifiersDown(); i++) await Task.Delay(20);

        SendCtrlV();
    }

    public async Task PasteRemotePairAsync(IClipboardWriter writer, string id, string password, bool sendEnter = false)
    {
        if (_target == IntPtr.Zero || !NativeMethods.IsWindow(_target)) return;
        NativeMethods.SetForegroundWindow(_target);
        await Task.Delay(60);

        for (int i = 0; i < 25 && ModifiersDown(); i++) await Task.Delay(20);

        // 1. Paste ID
        writer.WriteText(id);
        SendCtrlV();
        await Task.Delay(90);

        // 2. Send TAB key to jump from Partner ID to Password input field
        var tabInputs = new[]
        {
            NativeMethods.Key(NativeMethods.VK_TAB, up: false),
            NativeMethods.Key(NativeMethods.VK_TAB, up: true),
        };
        NativeMethods.SendInput((uint)tabInputs.Length, tabInputs, Marshal.SizeOf<NativeMethods.INPUT>());
        await Task.Delay(90);

        // 3. Paste Password
        writer.WriteText(password);
        SendCtrlV();

        if (sendEnter)
        {
            await Task.Delay(90);
            var enterInputs = new[]
            {
                NativeMethods.Key(0x0D /* VK_RETURN */, up: false),
                NativeMethods.Key(0x0D, up: true),
            };
            NativeMethods.SendInput((uint)enterInputs.Length, enterInputs, Marshal.SizeOf<NativeMethods.INPUT>());
        }
    }

    private static void SendCtrlV()
    {
        var inputs = new[]
        {
            NativeMethods.Key(NativeMethods.VK_CONTROL, up: false),
            NativeMethods.Key(NativeMethods.VK_V, up: false),
            NativeMethods.Key(NativeMethods.VK_V, up: true),
            NativeMethods.Key(NativeMethods.VK_CONTROL, up: true),
        };
        NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private static bool ModifiersDown() =>
        IsDown(NativeMethods.VK_SHIFT) || IsDown(NativeMethods.VK_MENU) || IsDown(NativeMethods.VK_LWIN) || IsDown(NativeMethods.VK_RWIN);

    private static bool IsDown(ushort vk) => (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;
}
