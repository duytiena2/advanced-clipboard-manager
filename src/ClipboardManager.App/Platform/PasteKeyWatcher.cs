using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using ClipboardManager.App.Native;

namespace ClipboardManager.App.Platform;

/// <summary>
/// Low-level keyboard hook that reports when the user pastes (Ctrl+V, Shift+Insert) in any app. Keys are only observed,
/// never blocked or recorded. Installed only while a paste stack is running.
/// </summary>
internal sealed class PasteKeyWatcher : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYUP = 0x0105;
    private const uint LLKHF_INJECTED = 0x10;
    private const int VK_INSERT = 0x2D;

    private readonly HookProc _proc; // keep the delegate alive while the hook is installed
    private IntPtr _hook;

    /// <summary>
    /// Raised on the installing (UI) thread when a paste keystroke goes down, before the target app has read the
    /// clipboard — handlers must return immediately and change the clipboard only after a short delay.
    /// </summary>
    public event Action? Pasted;

    public PasteKeyWatcher()
    {
        _proc = OnKey;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not install the keyboard hook.");
    }

    private IntPtr OnKey(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            bool up = wParam == WM_KEYUP || wParam == WM_SYSKEYUP;
            bool pasteKey = info.vkCode is NativeMethods.VK_V or VK_INSERT;
            if (pasteKey && up)
            {
                _keyHeld = false;
            }
            // Keystrokes injected by software (including our own Quick Paste) are ignored: only the user's own pastes count.
            else if (pasteKey && !_keyHeld && (info.flags & LLKHF_INJECTED) == 0)
            {
                bool ctrl = IsDown(NativeMethods.VK_CONTROL), shift = IsDown(NativeMethods.VK_SHIFT), alt = IsDown(NativeMethods.VK_MENU);
                bool paste = (info.vkCode == NativeMethods.VK_V && ctrl && !alt) || (info.vkCode == VK_INSERT && shift && !ctrl && !alt);
                if (paste)
                {
                    _keyHeld = true; // auto-repeat while held counts once
                    Pasted?.Invoke();
                }
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private bool _keyHeld;

    private static bool IsDown(int vk) => (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;

    public void Dispose()
    {
        if (_hook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
