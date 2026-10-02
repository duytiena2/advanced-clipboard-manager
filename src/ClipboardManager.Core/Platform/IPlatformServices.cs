using ClipboardManager.Core.Models;

namespace ClipboardManager.Core.Platform;

// OS-specific adapters. The Windows app implements these with Win32 APIs;
// a future macOS/Linux front end (e.g. Avalonia) provides its own implementations
// and reuses everything else in Core unchanged.

/// <summary>Raises <see cref="ContentCaptured"/> whenever the OS clipboard changes.</summary>
public interface IClipboardMonitor : IDisposable
{
    event EventHandler<CapturedContent>? ContentCaptured;
    void Start();
    void Stop();
}

/// <summary>Writes an item back to the OS clipboard without it being re-captured as new.</summary>
public interface IClipboardWriter
{
    void Write(ClipboardItem item, string dataFolder);
    void WriteText(string text);
}

/// <summary>Registers a system-wide shortcut.</summary>
public interface IHotkeyService : IDisposable
{
    /// <returns>false when another application already owns the shortcut.</returns>
    bool Register(string gesture, Action onPressed);
}

/// <summary>Sends a paste keystroke to the application that was active before the palette opened.</summary>
public interface IPasteSimulator
{
    void PasteIntoPreviousWindow();
}

public interface IClock
{
    DateTimeOffset Now { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.UtcNow;
}
