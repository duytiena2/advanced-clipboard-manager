using System;
using System.Collections.Specialized;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;
using ClipboardManager.App.Native;
using ClipboardManager.Core.Models;
using ClipboardManager.Core.Platform;
using WpfClipboard = System.Windows.Clipboard;

namespace ClipboardManager.App.Platform;

internal sealed class WindowsClipboardWriter : IClipboardWriter
{
    public void Write(ClipboardPayload payload)
    {
        var data = new DataObject();
        if (payload.ImagePng is { Length: > 0 } png)
        {
            using var ms = new MemoryStream(png);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            data.SetImage(bmp);
        }
        else if (payload.Files is { Count: > 0 } files)
        {
            var list = new StringCollection();
            foreach (var f in files) list.Add(f);
            data.SetFileDropList(list);
        }
        else
        {
            data.SetText(payload.Text ?? "", TextDataFormat.UnicodeText);
            // Raw CF_HTML exactly as captured; WPF writes it back as UTF-8, so its byte offsets stay valid.
            if (!string.IsNullOrEmpty(payload.Html)) data.SetData(DataFormats.Html, payload.Html);
            if (!string.IsNullOrEmpty(payload.Rtf)) data.SetData(DataFormats.Rtf, payload.Rtf);
        }

        if (payload.IsSensitive)
        {
            // Keep secrets out of Windows' own clipboard history and cloud clipboard.
            data.SetData("CanIncludeInClipboardHistory", new MemoryStream(BitConverter.GetBytes(0)));
            data.SetData("CanUploadToCloudClipboard", new MemoryStream(BitConverter.GetBytes(0)));
        }
        SetWithRetry(data);
    }

    public void WriteText(string text)
    {
        var data = new DataObject();
        data.SetText(text, TextDataFormat.UnicodeText);
        SetWithRetry(data);
    }

    private static void SetWithRetry(DataObject data)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                WpfClipboard.SetDataObject(data, copy: true);
                ClipboardSelfWrite.LastSequence = NativeMethods.GetClipboardSequenceNumber();
                return;
            }
            catch (COMException) when (attempt < 5)
            {
                Thread.Sleep(40 * (attempt + 1)); // another app holds the clipboard open
            }
        }
    }
}
