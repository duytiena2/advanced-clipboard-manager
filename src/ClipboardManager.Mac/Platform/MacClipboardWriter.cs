using System;
using System.Runtime.InteropServices;
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
            WriteText(payload.Text);
        }
    }

    public void WriteText(string text)
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

                        var typeStr = MacNative.CreateNSString("public.utf8-plain-text");
                        var nsStr = MacNative.CreateNSString(text);
                        var selSetString = MacNative.sel_registerName("setString:forType:");
                        MacNative.objc_msgSend(pb, selSetString, nsStr, typeStr);
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

        GCHandle handle = default;
        var pool = MacNative.objc_autoreleasePoolPush();
        try
        {
            var clsPb = MacNative.objc_getClass("NSPasteboard");
            var selGen = MacNative.sel_registerName("generalPasteboard");
            var pb = MacNative.objc_msgSend(clsPb, selGen);
            if (pb == IntPtr.Zero) return;

            var selClear = MacNative.sel_registerName("clearContents");
            MacNative.objc_msgSend(pb, selClear);

            handle = GCHandle.Alloc(png, GCHandleType.Pinned);
            IntPtr pBytes = handle.AddrOfPinnedObject();

            var clsData = MacNative.objc_getClass("NSData");
            var selDataWithBytes = MacNative.sel_registerName("dataWithBytes:length:");
            var nsData = MacNative.objc_msgSend(clsData, selDataWithBytes, pBytes, (IntPtr)png.Length);

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
            if (handle.IsAllocated) handle.Free();
            MacNative.objc_autoreleasePoolPop(pool);
        }
    }
}
