using System;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using ClipboardManager.Core.Models;
using ClipboardManager.Core.Platform;
using ClipboardManager.Mac.Native;

namespace ClipboardManager.Mac.Platform;

internal sealed class MacClipboardWriter : IClipboardWriter
{
    private static IClipboard? GetClipboard()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow?.Clipboard;
        }
        return null;
    }

    public void Write(ClipboardPayload payload)
    {
        if (payload.ImagePng is { Length: > 0 } && OperatingSystem.IsMacOS())
        {
            WriteImage(payload.ImagePng);
        }
        else if (payload.Text is not null)
        {
            WriteTextWithFormatting(payload.Text, payload.Html, payload.Rtf);
        }
    }

    public void WriteText(string text) => WriteTextWithFormatting(text, null, null);

    public void WriteTextWithFormatting(string text, string? html, string? rtf)
    {
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                var pool = MacNative.objc_autoreleasePoolPush();
                try
                {
                    var clsPb = MacNative.objc_getClass("NSPasteboard");
                    var selGen = MacNative.sel_registerName("generalPasteboard");
                    var pb = MacNative.objc_msgSend(clsPb, selGen);
                    if (pb != IntPtr.Zero)
                    {
                        var selClear = MacNative.sel_registerName("clearContents");
                        MacNative.objc_msgSend(pb, selClear);

                        var selSetString = MacNative.sel_registerName("setString:forType:");
                        var selSetData = MacNative.sel_registerName("setData:forType:");

                        // 1. Plain text
                        var typeStr = MacNative.CreateNSString("public.utf8-plain-text");
                        var nsStr = MacNative.CreateNSString(text);
                        MacNative.objc_msgSend(pb, selSetString, nsStr, typeStr);

                        // 2. HTML if available
                        if (!string.IsNullOrEmpty(html))
                        {
                            var htmlBytes = Encoding.UTF8.GetBytes(html);
                            var nsHtmlData = CreateNSData(htmlBytes);
                            if (nsHtmlData != IntPtr.Zero)
                            {
                                var typeHtml = MacNative.CreateNSString("public.html");
                                MacNative.objc_msgSend(pb, selSetData, nsHtmlData, typeHtml);
                            }
                        }

                        // 3. RTF if available
                        if (!string.IsNullOrEmpty(rtf))
                        {
                            var rtfBytes = Encoding.UTF8.GetBytes(rtf);
                            var nsRtfData = CreateNSData(rtfBytes);
                            if (nsRtfData != IntPtr.Zero)
                            {
                                var typeRtf = MacNative.CreateNSString("public.rtf");
                                MacNative.objc_msgSend(pb, selSetData, nsRtfData, typeRtf);
                            }
                        }

                        return;
                    }
                }
                finally
                {
                    MacNative.objc_autoreleasePoolPop(pool);
                }
            }

            var cb = GetClipboard();
            cb?.SetTextAsync(text).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MacClipboardWriter] WriteText error: {ex.Message}");
        }
    }

    public void WriteImage(byte[] png)
    {
        if (!OperatingSystem.IsMacOS() || png is null || png.Length == 0) return;

        var pool = MacNative.objc_autoreleasePoolPush();
        try
        {
            var clsPb = MacNative.objc_getClass("NSPasteboard");
            var selGen = MacNative.sel_registerName("generalPasteboard");
            var pb = MacNative.objc_msgSend(clsPb, selGen);
            if (pb == IntPtr.Zero) return;

            var selClear = MacNative.sel_registerName("clearContents");
            MacNative.objc_msgSend(pb, selClear);

            var nsData = CreateNSData(png);
            if (nsData != IntPtr.Zero)
            {
                var typePng = MacNative.CreateNSString("public.png");
                var selSetData = MacNative.sel_registerName("setData:forType:");
                MacNative.objc_msgSend(pb, selSetData, nsData, typePng);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MacClipboardWriter] WriteImage error: {ex.Message}");
        }
        finally
        {
            MacNative.objc_autoreleasePoolPop(pool);
        }
    }

    private static IntPtr CreateNSData(byte[] bytes)
    {
        if (bytes.Length == 0) return IntPtr.Zero;
        GCHandle handle = default;
        try
        {
            handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            IntPtr pBytes = handle.AddrOfPinnedObject();
            var clsData = MacNative.objc_getClass("NSData");
            var selDataWithBytes = MacNative.sel_registerName("dataWithBytes:length:");
            return MacNative.objc_msgSend(clsData, selDataWithBytes, pBytes, (IntPtr)bytes.Length);
        }
        finally
        {
            if (handle.IsAllocated) handle.Free();
        }
    }
}
