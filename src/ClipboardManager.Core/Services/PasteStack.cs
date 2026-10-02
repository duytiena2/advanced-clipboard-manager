using ClipboardManager.Core.Models;

namespace ClipboardManager.Core.Services;

/// <summary>
/// Ordered queue for "paste stack" mode: each paste in the target app consumes the current item and loads the next one,
/// which makes filling a form field by field a matter of Ctrl+V, Tab, Ctrl+V…
/// </summary>
public sealed class PasteStack
{
    private readonly List<ClipboardItem> _items;
    private int _index;

    public PasteStack(IEnumerable<ClipboardItem> items)
    {
        _items = items.ToList();
        if (_items.Count == 0) throw new ArgumentException("A paste stack needs at least one item.", nameof(items));
    }

    public int Count => _items.Count;

    /// <summary>1-based position of <see cref="Current"/> (for "2 / 5" status text).</summary>
    public int Position => Math.Min(_index + 1, _items.Count);

    public int Remaining => Math.Max(0, _items.Count - _index);

    public bool IsFinished => _index >= _items.Count;

    /// <summary>The item that is on the clipboard now (the next paste), or null when finished.</summary>
    public ClipboardItem? Current => IsFinished ? null : _items[_index];

    /// <summary>The current item was pasted: move on. Returns the next item, or null when the stack is done.</summary>
    public ClipboardItem? Advance()
    {
        if (!IsFinished) _index++;
        return Current;
    }
}
