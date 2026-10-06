using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ClipboardManager.Core.Platform;
using ZXing;
using ZXing.Common;

namespace ClipboardManager.App.Platform;

/// <summary>
/// Scans 1D and 2D barcodes (QR codes, Data Matrix, Code 128, etc.) from images using ZXing.Net.
/// </summary>
public sealed class WindowsBarcodeScanner : IBarcodeScanner
{
    private static readonly BarcodeFormat[] SupportedFormats =
    [
        BarcodeFormat.QR_CODE,
        BarcodeFormat.DATA_MATRIX,
        BarcodeFormat.AZTEC,
        BarcodeFormat.PDF_417,
        BarcodeFormat.CODE_128,
        BarcodeFormat.CODE_39,
        BarcodeFormat.EAN_13,
        BarcodeFormat.EAN_8,
        BarcodeFormat.UPC_A,
        BarcodeFormat.UPC_E,
        BarcodeFormat.ITF,
        BarcodeFormat.CODABAR
    ];

    public bool IsAvailable => true;

    public Task<IReadOnlyList<string>> ScanAsync(byte[] imageBytes, CancellationToken cancellationToken = default)
    {
        if (imageBytes is null || imageBytes.Length == 0)
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        return Task.Run<IReadOnlyList<string>>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var results = new List<string>();

            // Attempt 1: System.Drawing bitmap decoding (fast direct memory lock)
            try
            {
                using var ms = new MemoryStream(imageBytes);
                using var bmp = new System.Drawing.Bitmap(ms);

                var rect = new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height);
                var data = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                try
                {
                    int length = Math.Abs(data.Stride) * bmp.Height;
                    byte[] pixels = new byte[length];
                    Marshal.Copy(data.Scan0, pixels, 0, length);

                    var luminance = new RGBLuminanceSource(pixels, bmp.Width, bmp.Height, RGBLuminanceSource.BitmapFormat.BGRA32);
                    var reader = CreateReader();

                    cancellationToken.ThrowIfCancellationRequested();
                    var decodedList = reader.DecodeMultiple(luminance);
                    if (decodedList is not null)
                    {
                        foreach (var dec in decodedList)
                        {
                            if (!string.IsNullOrWhiteSpace(dec.Text) && !results.Contains(dec.Text.Trim()))
                            {
                                results.Add(dec.Text.Trim());
                            }
                        }
                    }
                }
                finally
                {
                    bmp.UnlockBits(data);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                // Fallback attempt: WPF BitmapDecoder
                try
                {
                    results.AddRange(ScanWithWpfDecoder(imageBytes, cancellationToken));
                }
                catch (OperationCanceledException) { throw; }
                catch
                {
                    // Ignore decode failures on corrupt image payloads
                }
            }

            return results;
        }, cancellationToken);
    }

    private static BarcodeReaderGeneric CreateReader() => new()
    {
        AutoRotate = true,
        Options = new DecodingOptions
        {
            TryHarder = true,
            TryInverted = true,
            PossibleFormats = SupportedFormats
        }
    };

    private static List<string> ScanWithWpfDecoder(byte[] imageBytes, CancellationToken cancellationToken)
    {
        var list = new List<string>();
        using var stream = new MemoryStream(imageBytes);
        var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(
            stream,
            System.Windows.Media.Imaging.BitmapCreateOptions.None,
            System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count == 0) return list;

        var frame = decoder.Frames[0];
        var converted = new System.Windows.Media.Imaging.FormatConvertedBitmap(
            frame,
            System.Windows.Media.PixelFormats.Bgra32,
            null,
            0);

        int width = converted.PixelWidth;
        int height = converted.PixelHeight;
        int stride = width * 4;
        byte[] pixels = new byte[height * stride];
        converted.CopyPixels(pixels, stride, 0);

        cancellationToken.ThrowIfCancellationRequested();
        var luminance = new RGBLuminanceSource(pixels, width, height, RGBLuminanceSource.BitmapFormat.BGRA32);
        var reader = CreateReader();

        var decodedList = reader.DecodeMultiple(luminance);
        if (decodedList is not null)
        {
            foreach (var dec in decodedList)
            {
                if (!string.IsNullOrWhiteSpace(dec.Text) && !list.Contains(dec.Text.Trim()))
                {
                    list.Add(dec.Text.Trim());
                }
            }
        }
        return list;
    }
}
