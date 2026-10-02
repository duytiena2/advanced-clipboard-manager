using System.Globalization;
using ClipboardManager.Core.Models;

namespace ClipboardManager.Core.Search;

/// <summary>
/// Parsed search input. Supports free text plus filters:
/// type:sql | type:code | workspace:backend | pinned:true | sensitive:false | before:2026-10-01 | after:2026-09-01
/// Quoted phrases ("docker compose") are kept together.
/// </summary>
public sealed class SearchQuery
{
    public List<string> Terms { get; } = new();
    public ContentKind? Kind { get; set; }
    public string? Subtype { get; set; }
    public string? Workspace { get; set; }
    public bool? Pinned { get; set; }
    public bool? Sensitive { get; set; }
    public DateTimeOffset? Before { get; set; }
    public DateTimeOffset? After { get; set; }

    public bool IsEmpty => Terms.Count == 0 && Kind is null && Subtype is null && Workspace is null &&
                           Pinned is null && Sensitive is null && Before is null && After is null;

    public static SearchQuery Parse(string? input)
    {
        var q = new SearchQuery();
        if (string.IsNullOrWhiteSpace(input)) return q;

        foreach (var token in Tokenize(input))
        {
            int colon = token.IndexOf(':');
            if (colon > 0 && colon < token.Length - 1 && !token.StartsWith('"'))
            {
                var key = token[..colon].ToLowerInvariant();
                var value = token[(colon + 1)..].Trim('"');
                if (q.TryApplyFilter(key, value)) continue;
            }
            var term = token.Trim('"').Trim();
            if (term.Length > 0) q.Terms.Add(term);
        }
        return q;
    }

    private bool TryApplyFilter(string key, string value)
    {
        switch (key)
        {
            case "type":
            case "is":
                if (Enum.TryParse<ContentKind>(value, ignoreCase: true, out var kind)) Kind = kind;
                else if (value.Equals("link", StringComparison.OrdinalIgnoreCase) || value.Equals("links", StringComparison.OrdinalIgnoreCase)) Kind = ContentKind.Url;
                else if (value.Equals("file", StringComparison.OrdinalIgnoreCase)) Kind = ContentKind.Files;
                else Subtype = value.ToLowerInvariant();
                return true;
            case "workspace":
            case "ws":
                Workspace = value;
                return true;
            case "pinned":
                Pinned = ParseBool(value);
                return Pinned is not null;
            case "sensitive":
                Sensitive = ParseBool(value);
                return Sensitive is not null;
            case "before":
                Before = ParseDate(value);
                return Before is not null;
            case "after":
                After = ParseDate(value);
                return After is not null;
            default:
                return false; // e.g. "http://…" or "C:\…" stay as free text
        }
    }

    private static bool? ParseBool(string v) => v.ToLowerInvariant() switch
    {
        "true" or "yes" or "1" => true,
        "false" or "no" or "0" => false,
        _ => null,
    };

    private static DateTimeOffset? ParseDate(string v) =>
        DateTime.TryParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var d)
            ? new DateTimeOffset(d)
            : null;

    private static IEnumerable<string> Tokenize(string input)
    {
        var sb = new System.Text.StringBuilder();
        bool inQuotes = false;
        foreach (var c in input)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                sb.Append(c);
                continue;
            }
            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (sb.Length > 0) { yield return sb.ToString(); sb.Clear(); }
                continue;
            }
            sb.Append(c);
        }
        if (sb.Length > 0) yield return sb.ToString();
    }
}
