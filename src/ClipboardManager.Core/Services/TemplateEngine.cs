using System.Globalization;
using System.Text;

namespace ClipboardManager.Core.Services;

/// <summary>Values available to a snippet template.</summary>
public sealed class TemplateContext
{
    /// <summary>Local time used for {date}/{time}.</summary>
    public DateTime Now { get; init; } = DateTime.Now;
    public CultureInfo Culture { get; init; } = CultureInfo.CurrentCulture;
    /// <summary>Current clipboard text for {clipboard} (read lazily, only when the template uses it).</summary>
    public Func<string?> Clipboard { get; init; } = () => null;
}

/// <summary>
/// Expands snippet variables:
/// {date} {time} {datetime} — current local date/time in the user's format;
/// {date:yyyy-MM-dd} {time:HH:mm:ss} — custom .NET format;
/// {clipboard} — current clipboard text; {uuid} — a new GUID; {{ and }} — literal braces.
/// Unknown variables are left unchanged.
/// </summary>
public static class TemplateEngine
{
    public static IReadOnlyList<(string Variable, string Description)> Variables { get; } = new[]
    {
        ("{date}", "today's date"),
        ("{time}", "current time"),
        ("{datetime}", "date and time"),
        ("{date:dd/MM/yyyy}", "date in a custom format"),
        ("{clipboard}", "current clipboard text"),
        ("{uuid}", "a new unique id"),
    };

    public static bool HasVariables(string template) => template.Contains('{');

    public static string Expand(string template, TemplateContext? ctx = null)
    {
        ctx ??= new TemplateContext();
        var sb = new StringBuilder(template.Length);
        string? clipboard = null;
        string Clipboard() => clipboard ??= ctx.Clipboard() ?? "";

        for (int i = 0; i < template.Length; i++)
        {
            char c = template[i];
            if (c == '{' && i + 1 < template.Length && template[i + 1] == '{') { sb.Append('{'); i++; continue; }
            if (c == '}' && i + 1 < template.Length && template[i + 1] == '}') { sb.Append('}'); i++; continue; }
            if (c != '{') { sb.Append(c); continue; }

            int end = template.IndexOf('}', i + 1);
            if (end < 0) { sb.Append(template, i, template.Length - i); break; }
            var token = template[(i + 1)..end];
            int colon = token.IndexOf(':');
            var name = (colon < 0 ? token : token[..colon]).Trim().ToLowerInvariant();
            var format = colon < 0 ? null : token[(colon + 1)..];

            string? value = name switch
            {
                "date" => Format(ctx.Now, format ?? "d", ctx.Culture),
                "time" => Format(ctx.Now, format ?? "t", ctx.Culture),
                "datetime" => Format(ctx.Now, format ?? "g", ctx.Culture),
                "uuid" or "guid" when format is null => Guid.NewGuid().ToString(),
                "clipboard" when format is null => Clipboard(),
                _ => null,
            };
            if (value is null) sb.Append(template, i, end - i + 1); // unknown: keep as typed
            else sb.Append(value);
            i = end;
        }
        return sb.ToString();
    }

    private static string? Format(DateTime when, string format, CultureInfo culture)
    {
        try { return when.ToString(format, culture); }
        catch (FormatException) { return null; }
    }
}
