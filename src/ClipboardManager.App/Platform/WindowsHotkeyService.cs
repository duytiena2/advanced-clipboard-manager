using System;
using System.Collections.Generic;
using System.Windows.Input;
using ClipboardManager.App.Native;
using ClipboardManager.Core.Platform;

namespace ClipboardManager.App.Platform;

/// <summary>Global shortcuts via RegisterHotKey, e.g. "Ctrl+Shift+V".</summary>
internal sealed class WindowsHotkeyService : IHotkeyService
{
    private readonly MessageWindow _window = new("ClipboardManager.Hotkeys");
    private readonly Dictionary<int, Action> _handlers = new();
    private int _nextId = 1; // applications must use ids 0x0000–0xBFFF (0xC000+ is reserved for shared DLLs)

    public WindowsHotkeyService()
    {
        _window.Message += (msg, wParam, _) =>
        {
            if (msg != NativeMethods.WM_HOTKEY) return false;
            if (_handlers.TryGetValue(wParam.ToInt32(), out var handler)) handler();
            return true;
        };
    }

    public bool Register(string gesture, Action onPressed)
    {
        if (!TryParse(gesture, out var mods, out var vk)) return false;
        int id = _nextId++;
        if (!NativeMethods.RegisterHotKey(_window.Handle, id, mods | NativeMethods.MOD_NOREPEAT, vk)) return false;
        _handlers[id] = onPressed;
        return true;
    }

    /// <summary>Parses "Ctrl+Shift+V", "Alt+Space", "Win+Shift+C" …</summary>
    public static bool TryParse(string gesture, out uint modifiers, out uint virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;
        if (string.IsNullOrWhiteSpace(gesture)) return false;
        foreach (var raw in gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl": case "control": modifiers |= NativeMethods.MOD_CONTROL; break;
                case "shift": modifiers |= NativeMethods.MOD_SHIFT; break;
                case "alt": modifiers |= NativeMethods.MOD_ALT; break;
                case "win": case "windows": modifiers |= NativeMethods.MOD_WIN; break;
                default:
                    if (!Enum.TryParse<Key>(raw, ignoreCase: true, out var key)) return false;
                    virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
                    break;
            }
        }
        return virtualKey != 0;
    }

    public void Dispose()
    {
        foreach (var id in _handlers.Keys) NativeMethods.UnregisterHotKey(_window.Handle, id);
        _handlers.Clear();
        _window.Dispose();
    }
}
