using ClipboardManager.Core.Models;

namespace ClipboardManager.Core.Services;

public enum MergeSeparator
{
    NewLine,
    DoubleNewLine,
    Space,
    Comma,
    Tab,
    Custom,
}

public static class MergeService
{
    public static string SeparatorText(MergeSeparator sep, string? custom = null) => sep switch
    {
        MergeSeparator.NewLine => Environment.NewLine,
        MergeSeparator.DoubleNewLine => Environment.NewLine + Environment.NewLine,
        MergeSeparator.Space => " ",
        MergeSeparator.Comma => ", ",
        MergeSeparator.Tab => "\t",
        MergeSeparator.Custom => custom ?? "",
        _ => Environment.NewLine,
    };

    /// <summary>Joins the text of the given items in order; non-text items (images) are skipped, files contribute their paths.</summary>
    public static string Merge(IEnumerable<ClipboardItem> items, MergeSeparator sep, string? custom = null)
    {
        var parts = items
            .Select(i => i.TextContent)
            .Where(t => !string.IsNullOrEmpty(t))
            .Select(t => t!.TrimEnd('\r', '\n'));
        return string.Join(SeparatorText(sep, custom), parts);
    }
}
