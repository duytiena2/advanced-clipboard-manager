using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using ClipboardManager.App.Native;
using ClipboardManager.Core.Platform;

namespace ClipboardManager.App.Platform;

/// <summary>Returns focus to the window that was active before Quick Paste opened and sends Ctrl+V to it.</summary>
internal sealed class WindowsPasteSimulator : IPasteSimulator
{
    private IntPtr _target;

    /// <summary>Call right before showing the palette.</summary>
    public void RememberForegroundWindow() => _target = NativeMethods.GetForegroundWindow();

    public void PasteIntoPreviousWindow() => _ = PasteAsync();

    private async Task PasteAsync()
    {
        if (_target == IntPtr.Zero || !NativeMethods.IsWindow(_target)) return;
        NativeMethods.SetForegroundWindow(_target);
        await Task.Delay(60); // let the target window take focus

        // Wait (briefly) until the user has released modifier keys, otherwise Ctrl+V becomes Ctrl+Shift+V etc.
        for (int i = 0; i < 25 && ModifiersDown(); i++) await Task.Delay(20);

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
