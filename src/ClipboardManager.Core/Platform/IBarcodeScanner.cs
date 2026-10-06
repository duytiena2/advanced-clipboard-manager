using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ClipboardManager.Core.Platform;

/// <summary>Decodes 1D and 2D barcodes (QR codes, Data Matrix, Code 128, etc.) from image data.</summary>
public interface IBarcodeScanner
{
    /// <summary>False when the scanner is not available or unsupported.</summary>
    bool IsAvailable { get; }

    /// <summary>Scans and decodes all barcodes/QR codes in the given image payload.</summary>
    Task<IReadOnlyList<string>> ScanAsync(byte[] imageBytes, CancellationToken cancellationToken = default);
}
