using System;
using System.Collections.Generic;
using Avalonia.Threading;
using ClipboardManager.Core.Models;
using ClipboardManager.Core.Platform;
using ClipboardManager.Core.Services;

namespace ClipboardManager.Mac.UI;

internal sealed class MacPasteStackController : IDisposable
{
    private readonly ClipboardService _svc;
    private readonly IClipboardWriter _writer;
    private readonly DispatcherTimer _advanceTimer;
    private PasteStack? _stack;

    public event Action<string?>? StatusChanged;

    public bool IsActive => _stack is not null;
    public int Position => _stack?.Position ?? 0;
    public int Count => _stack?.Count ?? 0;
    public string? CurrentTitle => _stack?.Current?.Title;

    public MacPasteStackController(ClipboardService svc, IClipboardWriter writer)
    {
        _svc = svc;
        _writer = writer;
        _advanceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _advanceTimer.Tick += (_, _) =>
        {
            _advanceTimer.Stop();
            Advance();
        };
    }

    public void Start(IReadOnlyList<ClipboardItem> items)
    {
        Stop();
        if (items.Count == 0) return;

        _stack = new PasteStack(items);
        if (_stack.Current is not null)
        {
            Load(_stack.Current);
        }
    }

    public void TriggerPasteAdvanced()
    {
        if (!IsActive) return;
        _advanceTimer.Stop();
        _advanceTimer.Start();
    }

    public void Advance()
    {
        if (_stack is null) return;
        var next = _stack.Advance();
        if (next is null)
        {
            Stop();
        }
        else
        {
            Load(next);
        }
    }

    private void Load(ClipboardItem item)
    {
        try
        {
            var payload = _svc.LoadPayload(item);
            if (payload is not null)
            {
                _writer.Write(payload);
                _svc.MarkUsed(item);
                StatusChanged?.Invoke($"Paste stack {_stack!.Position}/{_stack.Count}: {item.Title}");
            }
            else
            {
                Stop();
            }
        }
        catch
        {
            Stop();
        }
    }

    public void Stop()
    {
        _advanceTimer.Stop();
        _stack = null;
        StatusChanged?.Invoke(null);
    }

    public void Dispose()
    {
        Stop();
    }
}
