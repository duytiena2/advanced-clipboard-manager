using System;
using ClipboardManager.Core.Platform;

namespace ClipboardManager.Mac.Platform;

internal sealed class MacHotkeyService : IHotkeyService
{
    private Action? _onPressed;

    public bool Register(string gesture, Action onPressed)
    {
        _onPressed = onPressed;
        return true;
    }

    public void Trigger() => _onPressed?.Invoke();

    public void Dispose()
    {
        _onPressed = null;
    }
}
