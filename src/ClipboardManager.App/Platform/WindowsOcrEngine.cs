using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClipboardManager.Core.Platform;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace ClipboardManager.App.Platform;

/// <summary>
/// Windows' built-in OCR (Windows.Media.Ocr): offline, uses the OCR languages installed for the user's display languages
/// (Settings › Time &amp; language › Language; e.g. add Vietnamese to recognize Vietnamese text).
/// </summary>
internal sealed class WindowsOcrEngine : IOcrEngine
{
    private readonly OcrEngine? _engine;

    public WindowsOcrEngine()
    {
        try { _engine = OcrEngine.TryCreateFromUserProfileLanguages(); }
        catch (Exception ex) when (ex is TypeLoadException or System.Runtime.InteropServices.COMException or PlatformNotSupportedException)
        {
            _engine = null; // very old Windows build or OCR feature removed
        }
    }

    public bool IsAvailable => _engine is not null;

    public string? Language => _engine?.RecognizerLanguage.DisplayName;

    public async Task<string> RecognizeAsync(byte[] png, CancellationToken cancellationToken = default)
    {
        if (_engine is null) return "";
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream))
        {
            writer.WriteBytes(png);
            await writer.StoreAsync();
            writer.DetachStream();
        }
        stream.Seek(0);
        cancellationToken.ThrowIfCancellationRequested();

        var decoder = await BitmapDecoder.CreateAsync(stream);
        // OCR rejects images larger than MaxImageDimension: scale those down, keeping the aspect ratio.
        uint max = OcrEngine.MaxImageDimension;
        var transform = new BitmapTransform();
        if (decoder.PixelWidth > max || decoder.PixelHeight > max)
        {
            double scale = Math.Min((double)max / decoder.PixelWidth, (double)max / decoder.PixelHeight);
            transform.ScaledWidth = (uint)(decoder.PixelWidth * scale);
            transform.ScaledHeight = (uint)(decoder.PixelHeight * scale);
            transform.InterpolationMode = BitmapInterpolationMode.Fant;
        }
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            transform, ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);
        cancellationToken.ThrowIfCancellationRequested();

        var result = await _engine.RecognizeAsync(bitmap);
        return string.Join("\n", result.Lines.Select(l => l.Text));
    }
}
