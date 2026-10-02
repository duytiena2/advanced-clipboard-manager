namespace ClipboardManager.Core.Platform;

/// <summary>Local text recognition for images (Windows: Windows.Media.Ocr, offline).</summary>
public interface IOcrEngine
{
    /// <summary>False when no OCR language is installed.</summary>
    bool IsAvailable { get; }

    /// <returns>The recognized text (lines separated by newlines), "" when the image has no text.</returns>
    Task<string> RecognizeAsync(byte[] png, CancellationToken cancellationToken = default);
}
