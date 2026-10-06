using System;
using System.Threading;
using ClipboardManager.Core.Platform;
using ClipboardManager.Mac.Native;

namespace ClipboardManager.Mac.Platform;

internal sealed class MacPasteSimulator : IPasteSimulator
{
    private string? _targetAppName;

    public void CaptureTarget()
    {
        _targetAppName = MacNative.GetFrontmostAppName();
    }

    public bool IsRemoteDesktopTarget
    {
        get
        {
            if (string.IsNullOrEmpty(_targetAppName)) return false;
            var name = _targetAppName.ToLowerInvariant();
            return name.Contains("teamviewer")
                || name.Contains("anydesk")
                || name.Contains("remote desktop")
                || name.Contains("parsec")
                || name.Contains("rustdesk")
                || name.Contains("ultraviewer");
        }
    }

    public void PasteIntoPreviousWindow()
    {
        if (!OperatingSystem.IsMacOS()) return;

        // Slight delay to ensure target app has focus after palette hides
        Thread.Sleep(80);

        try
        {
            SendCmdV();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MacPasteSimulator] {ex.Message}");
        }
    }

    public void PasteRemotePair(IClipboardWriter writer, string id, string password, bool sendReturn = false)
    {
        if (!OperatingSystem.IsMacOS()) return;
        Thread.Sleep(80);

        try
        {
            // 1. Paste ID
            writer.WriteText(id);
            SendCmdV();
            Thread.Sleep(100);

            // 2. Press Tab (kVK_Tab = 0x30)
            SendKey(0x30);
            Thread.Sleep(100);

            // 3. Paste Password
            writer.WriteText(password);
            SendCmdV();

            if (sendReturn)
            {
                Thread.Sleep(100);
                SendKey(0x24); // kVK_Return = 0x24
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MacPasteSimulator] {ex.Message}");
        }
    }

    private static void SendCmdV()
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

    private static void SendKey(ushort vk)
    {
        var keyDown = MacNative.CGEventCreateKeyboardEvent(IntPtr.Zero, vk, true);
        var keyUp = MacNative.CGEventCreateKeyboardEvent(IntPtr.Zero, vk, false);

        MacNative.CGEventPost(MacNative.kCGHIDEventTap, keyDown);
        MacNative.CGEventPost(MacNative.kCGHIDEventTap, keyUp);

        MacNative.CFRelease(keyDown);
        MacNative.CFRelease(keyUp);
    }
}
