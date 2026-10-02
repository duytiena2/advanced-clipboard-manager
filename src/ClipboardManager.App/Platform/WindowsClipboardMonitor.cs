using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ClipboardManager.App.Native;
using ClipboardManager.Core.Models;
using ClipboardManager.Core.Platform;
using WpfClipboard = System.Windows.Clipboard;

namespace ClipboardManager.App.Platform;

/// <summary>
/// Listens with AddClipboardFormatListener (no polling). Respects the standard opt-out formats that
/// password managers set, and ignores writes made by this app itself.
/// </summary>
internal sealed class WindowsClipboardMonitor : IClipboardMonitor
{
    // Formats apps set to say "don't record this" (used by KeePass, 1Password, Windows clipboard history, etc.).
    private static readonly string[] OptOutFormats =
    {
        "ExcludeClipboardContentFromMonitorProcessing",
        "Clipboard Viewer Ignore",
    };

    private readonly MessageWindow _window;
    private readonly Dispatcher _dispatcher;
    private bool _listening;
    private uint _lastHandledSequence;

    public event EventHandler<CapturedContent>? ContentCaptured;

    public WindowsClipboardMonitor()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _window = new MessageWindow("ClipboardManager.ClipboardListener");
        _window.Message += OnMessage;
    }

    public void Start()
    {
        if (_listening) return;
        _listening = NativeMethods.AddClipboardFormatListener(_window.Handle);
        if (!_listening) throw new InvalidOperationException("AddClipboardFormatListener failed: " + Marshal.GetLastWin32Error());
    }

    public void Stop()
    {
        if (!_listening) return;
        NativeMethods.RemoveClipboardFormatListener(_window.Handle);
        _listening = false;
    }

    private bool OnMessage(int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg != NativeMethods.WM_CLIPBOARDUPDATE) return false;

        uint seq = NativeMethods.GetClipboardSequenceNumber();
        if (seq == _lastHandledSequence || seq == ClipboardSelfWrite.LastSequence) return true;
        _lastHandledSequence = seq;

        // Owner is the app that just wrote the clipboard; fall back to the foreground app.
        var source = NativeMethods.ProcessNameOfWindow(NativeMethods.GetClipboardOwner())
                     ?? NativeMethods.ProcessNameOfWindow(NativeMethods.GetForegroundWindow());

        // Let the source app finish writing all formats before reading.
        _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => _ = ReadAsync(source, seq)));
        return true;
    }

    private async Task ReadAsync(string? source, uint seq)
    {
        await Task.Delay(40);
        if (NativeMethods.GetClipboardSequenceNumber() != seq) return; // superseded by a newer copy

        IDataObject? data = null;
        for (int attempt = 0; attempt < 6 && data is null; attempt++)
        {
            try { data = WpfClipboard.GetDataObject(); }
            catch (COMException) { await Task.Delay(30 * (attempt + 1)); } // clipboard busy (CLIPBRD_E_CANT_OPEN)
            catch (ExternalException) { await Task.Delay(30 * (attempt + 1)); }
        }
        if (data is null) return;

        try
        {
            if (IsOptedOut(data)) return;
            var captured = Extract(data, source);
            if (captured is null) return;
            if (captured.ImagePng is null && captured.Text is null && captured.Files is null) return;
            ContentCaptured?.Invoke(this, captured);
        }
        catch (COMException) { /* clipboard changed while reading */ }
        catch (ExternalException) { }
        catch (OutOfMemoryException) { /* giant image */ }
    }

    private static bool IsOptedOut(IDataObject data)
    {
        foreach (var f in OptOutFormats)
        {
            if (data.GetDataPresent(f)) return true;
        }
        // CanIncludeInClipboardHistory = DWORD 0 means "do not keep in history".
        if (data.GetDataPresent("CanIncludeInClipboardHistory") && data.GetData("CanIncludeInClipboardHistory") is MemoryStream ms)
        {
            var buf = ms.ToArray();
            if (buf.Length >= 4 && BitConverter.ToInt32(buf, 0) == 0) return true;
        }
        return false;
    }

    /// <summary>Optional formats: a broken or huge one must not prevent capturing the text itself.</summary>
    private static string? TryGetString(IDataObject data, string format)
    {
        try
        {
            return data.GetDataPresent(format) && data.GetData(format) is string s && s.Length > 0 ? s : null;
        }
        catch (COMException) { return null; }
        catch (ExternalException) { return null; }
        catch (OutOfMemoryException) { return null; }
    }

    private static CapturedContent? Extract(IDataObject data, string? source)
    {
        // Priority: files → text → image (apps like Excel put both text and a picture; text is more useful).
        if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            return CapturedContent.FromFiles(files.ToList(), source);

        string? text = null;
        if (data.GetDataPresent(DataFormats.UnicodeText) && data.GetData(DataFormats.UnicodeText) is string unicode && unicode.Length > 0) text = unicode;
        else if (data.GetDataPresent(DataFormats.Text) && data.GetData(DataFormats.Text) is string ansi && ansi.Length > 0) text = ansi;
        if (text is not null)
            return new CapturedContent { Text = text, SourceApplication = source, Html = TryGetString(data, DataFormats.Html), Rtf = TryGetString(data, DataFormats.Rtf) };

        if (WpfClipboard.ContainsImage())
        {
            var bmp = WpfClipboard.GetImage();
            if (bmp is null) return null;
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            return CapturedContent.FromImage(ms.ToArray(), bmp.PixelWidth, bmp.PixelHeight, source);
        }
        return null;
    }

    public void Dispose()
    {
        Stop();
        _window.Dispose();
    }
}

/// <summary>Remembers the clipboard sequence number produced by our own writes so the monitor can skip them.</summary>
internal static class ClipboardSelfWrite
{
    public static uint LastSequence;
}
