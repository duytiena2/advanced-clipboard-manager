using System;
using System.Runtime.InteropServices;
using ClipboardManager.Core.Platform;
using ClipboardManager.Mac.Native;

namespace ClipboardManager.Mac.Platform;

internal sealed class MacHotkeyService : IHotkeyService
{
    private Action? _onPressed;
    private IntPtr _hotKeyRef = IntPtr.Zero;
    private IntPtr _handlerRef = IntPtr.Zero;
    private MacNative.EventHandlerDelegate? _eventHandlerDelegate; // Keep reference to prevent GC

    public bool Register(string gesture, Action onPressed)
    {
        _onPressed = onPressed;

        if (!OperatingSystem.IsMacOS())
        {
            return true;
        }

        try
        {
            Unregister();

            if (!TryParseGesture(gesture, out uint keyCode, out uint modifiers))
            {
                // Default to Cmd+Shift+V
                keyCode = MacNative.kVK_ANSI_V; // 0x09
                modifiers = 0x0100 | 0x0200;    // cmdKey | shiftKey
            }

            // Install Carbon event handler
            var eventType = new MacNative.EventTypeSpec
            {
                eventClass = 0x6b657962, // 'keyb' (kEventClassKeyboard)
                eventKind = 5            // kEventHotKeyPressed
            };

            _eventHandlerDelegate = (nextHandler, theEvent, userData) =>
            {
                _onPressed?.Invoke();
                return 0; // noErr
            };

            var target = MacNative.GetApplicationEventTarget();
            if (target == IntPtr.Zero) return false;

            int status = MacNative.InstallEventHandler(
                target,
                _eventHandlerDelegate,
                1,
                new[] { eventType },
                IntPtr.Zero,
                out _handlerRef);

            if (status != 0) return false;

            var hotKeyId = new MacNative.EventHotKeyID
            {
                signature = 0x4143504D, // 'ACPM'
                id = 1
            };

            status = MacNative.RegisterEventHotKey(
                keyCode,
                modifiers,
                hotKeyId,
                target,
                0,
                out _hotKeyRef);

            return status == 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MacHotkeyService] Failed to register hotkey: {ex.Message}");
            return false;
        }
    }

    public void Trigger() => _onPressed?.Invoke();

    private void Unregister()
    {
        if (_hotKeyRef != IntPtr.Zero)
        {
            MacNative.UnregisterEventHotKey(_hotKeyRef);
            _hotKeyRef = IntPtr.Zero;
        }
    }

    private static bool TryParseGesture(string gesture, out uint keyCode, out uint modifiers)
    {
        keyCode = 0;
        modifiers = 0;

        if (string.IsNullOrWhiteSpace(gesture)) return false;

        var parts = gesture.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;

        string keyPart = parts[^1].ToUpperInvariant();
        keyCode = keyPart switch
        {
            "A" => 0x00,
            "S" => 0x01,
            "D" => 0x02,
            "F" => 0x03,
            "H" => 0x04,
            "G" => 0x05,
            "Z" => 0x06,
            "X" => 0x07,
            "C" => 0x08,
            "V" => 0x09,
            "B" => 0x0B,
            "Q" => 0x0C,
            "W" => 0x0D,
            "E" => 0x0E,
            "R" => 0x0F,
            "Y" => 0x10,
            "T" => 0x11,
            "1" => 0x12,
            "2" => 0x13,
            "3" => 0x14,
            "4" => 0x15,
            "6" => 0x16,
            "5" => 0x17,
            "9" => 0x19,
            "7" => 0x1A,
            "8" => 0x1C,
            "0" => 0x1D,
            "O" => 0x1F,
            "U" => 0x20,
            "I" => 0x22,
            "P" => 0x23,
            "L" => 0x25,
            "J" => 0x26,
            "K" => 0x28,
            "SPACE" => 0x31,
            _ => 0x09 // fallback to V
        };

        for (int i = 0; i < parts.Length - 1; i++)
        {
            var mod = parts[i].ToLowerInvariant();
            if (mod is "cmd" or "command" or "super" or "win") modifiers |= 0x0100;       // cmdKey
            else if (mod is "shift") modifiers |= 0x0200;                                 // shiftKey
            else if (mod is "alt" or "option" or "opt") modifiers |= 0x0800;              // optionKey
            else if (mod is "ctrl" or "control") modifiers |= 0x1000;                     // controlKey
        }

        // If no modifier specified, default to Cmd+Shift
        if (modifiers == 0) modifiers = 0x0100 | 0x0200;

        return true;
    }

    public void Dispose()
    {
        Unregister();
        _onPressed = null;
        _eventHandlerDelegate = null;
    }
}
