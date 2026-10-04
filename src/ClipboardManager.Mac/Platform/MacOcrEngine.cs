using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ClipboardManager.Core.Platform;
using ClipboardManager.Mac.Native;

namespace ClipboardManager.Mac.Platform;

/// <summary>
/// macOS native OCR engine using Apple's Vision framework (VNRecognizeTextRequest).
/// Offline, fast, and supports multi-language recognition (English, Vietnamese, Japanese, Chinese, etc.).
/// </summary>
public sealed class MacOcrEngine : IOcrEngine
{
    private readonly bool _isAvailable;

    public MacOcrEngine()
    {
        if (!OperatingSystem.IsMacOS())
        {
            _isAvailable = false;
            return;
        }

        try
        {
            // Dynamically load Apple's Vision framework
            const int RTLD_LAZY = 1;
            var handle = MacNative.dlopen("/System/Library/Frameworks/Vision.framework/Vision", RTLD_LAZY);
            if (handle == IntPtr.Zero)
            {
                _isAvailable = false;
                return;
            }

            var clsReq = MacNative.objc_getClass("VNRecognizeTextRequest");
            var clsHandler = MacNative.objc_getClass("VNImageRequestHandler");
            _isAvailable = clsReq != IntPtr.Zero && clsHandler != IntPtr.Zero;
        }
        catch
        {
            _isAvailable = false;
        }
    }

    public bool IsAvailable => _isAvailable;

    public Task<string> RecognizeAsync(byte[] png, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable || png is null || png.Length == 0)
        {
            return Task.FromResult(string.Empty);
        }

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return RecognizeInternal(png, cancellationToken);
        }, cancellationToken);
    }

    private static string RecognizeInternal(byte[] png, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsMacOS()) return string.Empty;

        var pool = MacNative.objc_autoreleasePoolPush();
        GCHandle handle = default;
        try
        {
            var clsNSData = MacNative.objc_getClass("NSData");
            var clsNSDictionary = MacNative.objc_getClass("NSDictionary");
            var clsNSArray = MacNative.objc_getClass("NSArray");
            var clsVNImageRequestHandler = MacNative.objc_getClass("VNImageRequestHandler");
            var clsVNRecognizeTextRequest = MacNative.objc_getClass("VNRecognizeTextRequest");

            if (clsNSData == IntPtr.Zero || clsVNImageRequestHandler == IntPtr.Zero || clsVNRecognizeTextRequest == IntPtr.Zero)
            {
                return string.Empty;
            }

            handle = GCHandle.Alloc(png, GCHandleType.Pinned);
            IntPtr pBytes = handle.AddrOfPinnedObject();

            var selDataWithBytes = MacNative.sel_registerName("dataWithBytes:length:");
            var nsData = MacNative.objc_msgSend(clsNSData, selDataWithBytes, pBytes, (IntPtr)png.Length);
            if (nsData == IntPtr.Zero) return string.Empty;

            var selDictionary = MacNative.sel_registerName("dictionary");
            var emptyOptions = MacNative.objc_msgSend(clsNSDictionary, selDictionary);

            var selAlloc = MacNative.sel_registerName("alloc");
            var selInit = MacNative.sel_registerName("init");
            var reqAlloc = MacNative.objc_msgSend(clsVNRecognizeTextRequest, selAlloc);
            var request = MacNative.objc_msgSend(reqAlloc, selInit);
            if (request == IntPtr.Zero) return string.Empty;

            // VNRequestTextRecognitionLevelAccurate = 1
            var selSetLevel = MacNative.sel_registerName("setRecognitionLevel:");
            MacNative.objc_msgSend(request, selSetLevel, (IntPtr)1);

            // setUsesLanguageCorrection: YES
            var selSetLangCorrection = MacNative.sel_registerName("setUsesLanguageCorrection:");
            MacNative.objc_msgSend(request, selSetLangCorrection, (IntPtr)1);

            var selArrayWithObject = MacNative.sel_registerName("arrayWithObject:");
            var requestsArray = MacNative.objc_msgSend(clsNSArray, selArrayWithObject, request);

            var selInitWithData = MacNative.sel_registerName("initWithData:options:");
            var handlerAlloc = MacNative.objc_msgSend(clsVNImageRequestHandler, selAlloc);
            var imageHandler = MacNative.objc_msgSend(handlerAlloc, selInitWithData, nsData, emptyOptions);
            if (imageHandler == IntPtr.Zero) return string.Empty;

            cancellationToken.ThrowIfCancellationRequested();

            var selPerform = MacNative.sel_registerName("performRequests:error:");
            IntPtr error;
            bool ok = MacNative.objc_msgSend_bool_ptr_outptr(imageHandler, selPerform, requestsArray, out error);
            if (!ok) return string.Empty;

            cancellationToken.ThrowIfCancellationRequested();

            var selResults = MacNative.sel_registerName("results");
            var resultsArray = MacNative.objc_msgSend(request, selResults);
            if (resultsArray == IntPtr.Zero) return string.Empty;

            var selCount = MacNative.sel_registerName("count");
            long count = MacNative.objc_msgSend_long(resultsArray, selCount);
            if (count <= 0) return string.Empty;

            var selObjectAtIndex = MacNative.sel_registerName("objectAtIndex:");
            var selTopCandidates = MacNative.sel_registerName("topCandidates:");
            var selFirstObject = MacNative.sel_registerName("firstObject");
            var selString = MacNative.sel_registerName("string");

            var lines = new List<string>((int)Math.Min(count, 500));
            for (long i = 0; i < count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var observation = MacNative.objc_msgSend(resultsArray, selObjectAtIndex, (IntPtr)i);
                if (observation == IntPtr.Zero) continue;

                var candidates = MacNative.objc_msgSend(observation, selTopCandidates, (IntPtr)1);
                if (candidates == IntPtr.Zero) continue;

                var recognizedText = MacNative.objc_msgSend(candidates, selFirstObject);
                if (recognizedText == IntPtr.Zero) continue;

                var nsString = MacNative.objc_msgSend(recognizedText, selString);
                var text = MacNative.GetStringFromNSString(nsString);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    lines.Add(text);
                }
            }

            return string.Join("\n", lines);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MacOcrEngine] Recognition failed: {ex.Message}");
            return string.Empty;
        }
        finally
        {
            if (handle.IsAllocated) handle.Free();
            MacNative.objc_autoreleasePoolPop(pool);
        }
    }
}
