using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ClipboardManager.Core.Platform;
using ClipboardManager.Mac.Native;

namespace ClipboardManager.Mac.Platform;

/// <summary>
/// macOS native barcode and QR code scanner using Apple's Vision framework (VNDetectBarcodesRequest).
/// Offline, hardware-accelerated, and supports QR codes, Aztec, DataMatrix, Code 128, EAN, PDF417, etc.
/// </summary>
public sealed class MacBarcodeScanner : IBarcodeScanner
{
    private readonly bool _isAvailable;

    public MacBarcodeScanner()
    {
        if (!OperatingSystem.IsMacOS())
        {
            _isAvailable = false;
            return;
        }

        try
        {
            const int RTLD_LAZY = 1;
            var handle = MacNative.dlopen("/System/Library/Frameworks/Vision.framework/Vision", RTLD_LAZY);
            if (handle == IntPtr.Zero)
            {
                _isAvailable = false;
                return;
            }

            var clsReq = MacNative.objc_getClass("VNDetectBarcodesRequest");
            var clsHandler = MacNative.objc_getClass("VNImageRequestHandler");
            _isAvailable = clsReq != IntPtr.Zero && clsHandler != IntPtr.Zero;
        }
        catch
        {
            _isAvailable = false;
        }
    }

    public bool IsAvailable => _isAvailable;

    public Task<IReadOnlyList<string>> ScanAsync(byte[] imageBytes, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable || imageBytes is null || imageBytes.Length == 0)
        {
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }

        return Task.Run<IReadOnlyList<string>>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ScanInternal(imageBytes, cancellationToken);
        }, cancellationToken);
    }

    private static IReadOnlyList<string> ScanInternal(byte[] png, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsMacOS()) return Array.Empty<string>();

        var results = new List<string>();
        var pool = MacNative.objc_autoreleasePoolPush();
        GCHandle handle = default;
        try
        {
            var clsNSData = MacNative.objc_getClass("NSData");
            var clsNSDictionary = MacNative.objc_getClass("NSDictionary");
            var clsNSArray = MacNative.objc_getClass("NSArray");
            var clsVNImageRequestHandler = MacNative.objc_getClass("VNImageRequestHandler");
            var clsVNDetectBarcodesRequest = MacNative.objc_getClass("VNDetectBarcodesRequest");

            if (clsNSData == IntPtr.Zero || clsVNImageRequestHandler == IntPtr.Zero || clsVNDetectBarcodesRequest == IntPtr.Zero)
            {
                return results;
            }

            handle = GCHandle.Alloc(png, GCHandleType.Pinned);
            IntPtr pBytes = handle.AddrOfPinnedObject();

            var selDataWithBytes = MacNative.sel_registerName("dataWithBytes:length:");
            var nsData = MacNative.objc_msgSend(clsNSData, selDataWithBytes, pBytes, (IntPtr)png.Length);
            if (nsData == IntPtr.Zero) return results;

            var selDictionary = MacNative.sel_registerName("dictionary");
            var emptyOptions = MacNative.objc_msgSend(clsNSDictionary, selDictionary);

            var selAlloc = MacNative.sel_registerName("alloc");
            var selInit = MacNative.sel_registerName("init");
            var reqAlloc = MacNative.objc_msgSend(clsVNDetectBarcodesRequest, selAlloc);
            var request = MacNative.objc_msgSend(reqAlloc, selInit);
            if (request == IntPtr.Zero) return results;

            var selArrayWithObject = MacNative.sel_registerName("arrayWithObject:");
            var requestsArray = MacNative.objc_msgSend(clsNSArray, selArrayWithObject, request);

            var selInitWithData = MacNative.sel_registerName("initWithData:options:");
            var handlerAlloc = MacNative.objc_msgSend(clsVNImageRequestHandler, selAlloc);
            var imageHandler = MacNative.objc_msgSend(handlerAlloc, selInitWithData, nsData, emptyOptions);
            if (imageHandler == IntPtr.Zero) return results;

            cancellationToken.ThrowIfCancellationRequested();

            var selPerform = MacNative.sel_registerName("performRequests:error:");
            IntPtr error;
            bool ok = MacNative.objc_msgSend_bool_ptr_outptr(imageHandler, selPerform, requestsArray, out error);
            if (!ok) return results;

            cancellationToken.ThrowIfCancellationRequested();

            var selResults = MacNative.sel_registerName("results");
            var resultsArray = MacNative.objc_msgSend(request, selResults);
            if (resultsArray == IntPtr.Zero) return results;

            var selCount = MacNative.sel_registerName("count");
            long count = MacNative.objc_msgSend_long(resultsArray, selCount);
            if (count <= 0) return results;

            var selObjectAtIndex = MacNative.sel_registerName("objectAtIndex:");
            var selPayloadStringValue = MacNative.sel_registerName("payloadStringValue");

            for (long i = 0; i < count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var observation = MacNative.objc_msgSend(resultsArray, selObjectAtIndex, (IntPtr)i);
                if (observation == IntPtr.Zero) continue;

                var nsString = MacNative.objc_msgSend(observation, selPayloadStringValue);
                if (nsString == IntPtr.Zero) continue;

                var text = MacNative.GetStringFromNSString(nsString);
                if (!string.IsNullOrWhiteSpace(text) && !results.Contains(text.Trim()))
                {
                    results.Add(text.Trim());
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MacBarcodeScanner] Scan failed: {ex.Message}");
        }
        finally
        {
            if (handle.IsAllocated) handle.Free();
            MacNative.objc_autoreleasePoolPop(pool);
        }

        return results;
    }
}
