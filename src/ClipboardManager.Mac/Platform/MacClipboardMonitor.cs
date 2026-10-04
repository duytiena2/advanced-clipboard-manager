using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using ClipboardManager.Core.Models;
using ClipboardManager.Core.Platform;
using ClipboardManager.Mac.Native;

namespace ClipboardManager.Mac.Platform;

internal sealed class MacClipboardMonitor : IClipboardMonitor
{
    private DispatcherTimer? _timer;
    private string? _lastText;
    private long _lastChangeCount = -1;
    private bool _running;

    public event EventHandler<CapturedContent>? ContentCaptured;

    public void Start()
    {
        if (_running) return;
        _running = true;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _timer.Tick += async (_, _) => await CheckClipboardAsync();
        _timer.Start();
    }

    public void Stop()
    {
        _running = false;
        _timer?.Stop();
        _timer = null;
    }

    private async Task CheckClipboardAsync()
    {
        if (!_running) return;

        try
        {
            if (OperatingSystem.IsMacOS())
            {
                CheckMacPasteboard();
                return;
            }

            var cb = GetClipboard();
            if (cb is null) return;

            var text = await cb.GetTextAsync();
            if (!string.IsNullOrEmpty(text) && text != _lastText)
            {
                _lastText = text;
                ContentCaptured?.Invoke(this, CapturedContent.FromText(text));
            }
        }
        catch
        {
            // Ignore transient clipboard access errors
        }
    }

    private void CheckMacPasteboard()
    {
        if (!OperatingSystem.IsMacOS()) return;

        var pool = MacNative.objc_autoreleasePoolPush();
        try
        {
            var clsPb = MacNative.objc_getClass("NSPasteboard");
            var selGen = MacNative.sel_registerName("generalPasteboard");
            var pb = MacNative.objc_msgSend(clsPb, selGen);
            if (pb == IntPtr.Zero) return;

            var selChangeCount = MacNative.sel_registerName("changeCount");
            long changeCount = MacNative.objc_msgSend_long(pb, selChangeCount);
            if (changeCount == _lastChangeCount) return;
            _lastChangeCount = changeCount;

            // 1. Check for Image (public.png or public.tiff)
            var selDataForType = MacNative.sel_registerName("dataForType:");
            var typePng = MacNative.CreateNSString("public.png");
            var data = MacNative.objc_msgSend(pb, selDataForType, typePng);

            bool isTiff = false;
            if (data == IntPtr.Zero)
            {
                var typeTiff = MacNative.CreateNSString("public.tiff");
                data = MacNative.objc_msgSend(pb, selDataForType, typeTiff);
                isTiff = data != IntPtr.Zero;
            }

            if (data != IntPtr.Zero)
            {
                // Convert TIFF to PNG representation via NSBitmapImageRep if needed
                if (isTiff)
                {
                    var clsRep = MacNative.objc_getClass("NSBitmapImageRep");
                    var selRep = MacNative.sel_registerName("imageRepWithData:");
                    var rep = MacNative.objc_msgSend(clsRep, selRep, data);
                    if (rep != IntPtr.Zero)
                    {
                        var selUsingType = MacNative.sel_registerName("representationUsingType:properties:");
                        var selDict = MacNative.sel_registerName("dictionary");
                        var emptyDict = MacNative.objc_msgSend(MacNative.objc_getClass("NSDictionary"), selDict);
                        // 4 = NSBitmapImageFileTypePNG
                        var pngData = MacNative.objc_msgSend(rep, selUsingType, (IntPtr)4, emptyDict);
                        if (pngData != IntPtr.Zero)
                        {
                            data = pngData;
                        }
                    }
                }

                var selLen = MacNative.sel_registerName("length");
                var selBytes = MacNative.sel_registerName("bytes");
                long len = MacNative.objc_msgSend_long(data, selLen);
                if (len > 0)
                {
                    IntPtr pBytes = MacNative.objc_msgSend(data, selBytes);
                    byte[] bytes = new byte[len];
                    Marshal.Copy(pBytes, bytes, 0, (int)len);

                    int width = 0, height = 0;
                    if (bytes.Length >= 24 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
                    {
                        width = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
                        height = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
                    }

                    ContentCaptured?.Invoke(this, CapturedContent.FromImage(bytes, width, height));
                    return;
                }
            }

            // 2. Check for Text (public.utf8-plain-text)
            var selStringForType = MacNative.sel_registerName("stringForType:");
            var typeStr = MacNative.CreateNSString("public.utf8-plain-text");
            var nsStr = MacNative.objc_msgSend(pb, selStringForType, typeStr);
            var text = MacNative.GetStringFromNSString(nsStr);
            if (!string.IsNullOrEmpty(text) && text != _lastText)
            {
                _lastText = text;
                ContentCaptured?.Invoke(this, CapturedContent.FromText(text));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MacClipboardMonitor] Error checking pasteboard: {ex.Message}");
        }
        finally
        {
            MacNative.objc_autoreleasePoolPop(pool);
        }
    }

    private static IClipboard? GetClipboard()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow?.Clipboard;
        }
        return null;
    }

    public void Dispose()
    {
        Stop();
    }
}
