using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using ClipboardManager.App.Platform;
using ClipboardManager.Core.Models;
using ClipboardManager.Core.Platform;
using ClipboardManager.Core.Services;

namespace ClipboardManager.App.UI;

/// <summary>
/// Runs a <see cref="PasteStack"/>: puts the current item on the clipboard and, each time the user pastes anywhere,
/// loads the next one. Ends when the stack is used up, when the user copies something else, or on Stop.
/// </summary>
internal sealed class PasteStackController : IDisposable
{
    // Time the target app gets to read the clipboard after the paste key goes down, before the next item replaces it.
    private static readonly TimeSpan AdvanceDelay = TimeSpan.FromMilliseconds(250);

    private readonly ClipboardService _svc;
    private readonly IClipboardWriter _writer;
    private readonly DispatcherTimer _advance;
    private PasteStack? _stack;
    private PasteKeyWatcher? _watcher;

    /// <summary>Status line for the tray ("Paste stack 2/5: …"), or null when no stack is running.</summary>
    public event Action<string?>? StatusChanged;
    /// <summary>Something worth a notification (started, finished, cancelled, error).</summary>
    public event Action<string, bool>? Notify;

    public bool IsActive => _stack is not null;

    public PasteStackController(ClipboardService svc, IClipboardWriter writer)
    {
        _svc = svc;
        _writer = writer;
        _advance = new DispatcherTimer { Interval = AdvanceDelay };
        _advance.Tick += (_, _) => { _advance.Stop(); Advance(); };
    }

    public void Start(IReadOnlyList<ClipboardItem> items)
    {
        Stop(null);
        if (items.Count == 0) return;
        var stack = new PasteStack(items);
        try
        {
            _watcher = new PasteKeyWatcher();
        }
        catch (Win32Exception ex)
        {
            App.Log(ex);
            Notify?.Invoke("Paste stack unavailable: " + ex.Message, true);
            return;
        }
        _watcher.Pasted += () => { _advance.Stop(); _advance.Start(); };
        _stack = stack;
        if (!Load(stack.Current!)) return;
        Notify?.Invoke($"Paste stack ready: {stack.Count} items. Each Ctrl+V pastes the next one.", false);
    }

    private void Advance()
    {
        if (_stack is null) return;
        var next = _stack.Advance();
        if (next is null) Stop("Paste stack finished.");
        else Load(next);
    }

    private bool Load(ClipboardItem item)
    {
        try
        {
            var payload = _svc.LoadPayload(item);
            if (payload is null)
            {
                Stop("Paste stack stopped: an item has nothing to paste (missing file?).", warning: true);
                return false;
            }
            _writer.Write(payload);
            _svc.MarkUsed(item);
            StatusChanged?.Invoke($"Paste stack {_stack!.Position}/{_stack.Count}: {Short(item.Title)}");
            return true;
        }
        catch (Exception ex) when (ex is COMException or IOException or InvalidOperationException)
        {
            App.Log(ex);
            Stop("Paste stack stopped: could not write the clipboard.", warning: true);
            return false;
        }
    }

    /// <summary>The clipboard was changed by another app (the user copied something): the stack no longer applies.</summary>
    public void OnExternalClipboardChange()
    {
        if (IsActive) Stop("Paste stack cancelled: you copied something else.");
    }

    public void Stop(string? message, bool warning = false)
    {
        _advance.Stop();
        _watcher?.Dispose();
        _watcher = null;
        bool wasActive = _stack is not null;
        _stack = null;
        if (!wasActive) return;
        StatusChanged?.Invoke(null);
        if (message is not null) Notify?.Invoke(message, warning);
    }

    private static string Short(string s) => s.Length <= 28 ? s : s[..27] + "…";

    public void Dispose() => Stop(null);
}
