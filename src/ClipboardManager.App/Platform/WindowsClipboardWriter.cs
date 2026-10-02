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
    public void Write(ClipboardItem item, string dataFolder)
    {
        var data = new DataObject();
        switch (item.Kind)
        {
            case ContentKind.Image when item.BinaryPath is not null:
                var path = Path.Combine(dataFolder, item.BinaryPath);
                if (!File.Exists(path)) return;
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad; // don't keep the file locked
                bmp.UriSource = new Uri(path);
                bmp.EndInit();
                bmp.Freeze();
                data.SetImage(bmp);
                break;

            case ContentKind.Files when item.TextContent is not null:
                var list = new StringCollection();
                foreach (var f in item.TextContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)) list.Add(f);
                data.SetFileDropList(list);
                break;

            default:
                data.SetText(item.TextContent ?? "", TextDataFormat.UnicodeText);
                break;
        }

        if (item.IsSensitive)
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
