using System;
using System.Threading;
using ClipboardManager.Core.Platform;
using ClipboardManager.Mac.Native;

namespace ClipboardManager.Mac.Platform;

internal sealed class MacPasteSimulator : IPasteSimulator
{
    public void PasteIntoPreviousWindow()
    {
        if (!OperatingSystem.IsMacOS()) return;

        // Slight delay to ensure target app has focus after palette hides
        Thread.Sleep(80);

        try
        {
            var keyDown = MacNative.CGEventCreateKeyboardEvent(IntPtr.Zero, MacNative.kVK_ANSI_V, true);
            var keyUp = MacNative.CGEventCreateKeyboardEvent(IntPtr.Zero, MacNative.kVK_ANSI_V, false);

            MacNative.CGEventSetFlags(keyDown, MacNative.kCGEventFlagMaskCommand);
            MacNative.CGEventSetFlags(keyUp, MacNative.kCGEventFlagMaskCommand);

            MacNative.CGEventPost(MacNative.kCGHIDEventTap, keyDown);
            MacNative.CGEventPost(MacNative.kCGHIDEventTap, keyUp);

            MacNative.CFRelease(keyDown);
            MacNative.CFRelease(keyUp);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MacPasteSimulator] {ex.Message}");
        }
    }
}
