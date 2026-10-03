using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using ClipboardManager.Core.Models;

namespace ClipboardManager.Core.Search;

/// <summary>
/// Represents a structured filter chip extracted from or applied to the search query.
/// e.g. type:sql -> [SQL x], pinned:true -> [Pinned x]
/// </summary>
public sealed class SearchFilterChip : IEquatable<SearchFilterChip>
{
    public string Key { get; }
    public string Value { get; }
    public string Label { get; }
    public string RawSyntax { get; }

    public SearchFilterChip(string key, string value, string? label = null)
    {
        Key = NormalizeKey(key);
        Value = value.Trim().Trim('"');
        RawSyntax = Value.Contains(' ') ? $"{Key}:\"{Value}\"" : $"{Key}:{Value}";
        Label = label ?? FormatLabel(Key, Value);
    }

    public static string NormalizeKey(string key) => key.ToLowerInvariant() switch
    {
        "is" => "type",
        _ => key.ToLowerInvariant(),
    };

    public static string FormatLabel(string key, string value)
    {
        var normKey = NormalizeKey(key);
        var v = value.Trim().Trim('"');

        switch (normKey)
        {
            case "type":
                var vLower = v.ToLowerInvariant();
                return vLower switch
                {
                    "sql" => "SQL",
                    "json" => "JSON",
                    "xml" => "XML",
                    "yaml" => "YAML",
                    "shell" => "Shell",
                    "markdown" => "Markdown",
                    "link" or "links" => "URL",
                    "file" or "files" => "Files",
                    "image" or "images" => "Images",
                    "snippet" or "snippets" => "Snippets",
                    _ => Enum.TryParse<ContentKind>(v, ignoreCase: true, out var kind)
                        ? kind.ToString()
                        : (v.Length > 0 ? char.ToUpperInvariant(v[0]) + v[1..] : v)
                };

            case "pinned":
                return IsTrue(v) ? "Pinned" : "Unpinned";

            case "sensitive":
                return IsTrue(v) ? "Sensitive" : "Not Sensitive";

            case "after":
                return $"After {v}";

            case "before":
                return $"Before {v}";

            default:
                return $"{normKey}:{v}";
        }
    }

    public static bool IsFilterToken(string token, out string key, out string value)
    {
        key = "";
        value = "";
        int colon = token.IndexOf(':');
        if (colon <= 0 || colon >= token.Length - 1 || token.StartsWith('"')) return false;

        var k = token[..colon].ToLowerInvariant();
        var v = token[(colon + 1)..].Trim('"');

        if (k is "type" or "is" or "pinned" or "sensitive" or "before" or "after")
        {
            if (k is "pinned" or "sensitive" && !IsValidBool(v)) return false;
            if (k is "before" or "after" && !IsValidDate(v)) return false;
            if (string.IsNullOrWhiteSpace(v)) return false;

            key = NormalizeKey(k);
            value = v;
            return true;
        }

        return false;
    }

    public static bool TryParseFilter(string token, out SearchFilterChip? chip)
    {
        chip = null;
        if (IsFilterToken(token, out var key, out var value))
        {
            chip = new SearchFilterChip(key, value);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Parses input text, extracts any recognized filter tokens into chips,
    /// and returns the remaining free text.
    /// </summary>
    public static (List<SearchFilterChip> Chips, string RemainingText) ExtractFilters(string? input)
    {
        var chips = new List<SearchFilterChip>();
        var freeTerms = new List<string>();

        if (string.IsNullOrWhiteSpace(input))
            return (chips, "");

        foreach (var token in Tokenize(input))
        {
            if (TryParseFilter(token, out var chip) && chip is not null)
            {
                // Replace any existing chip with the same key
                int existingIdx = chips.FindIndex(c => c.Key == chip.Key);
                if (existingIdx >= 0) chips[existingIdx] = chip;
                else chips.Add(chip);
            }
            else
            {
                freeTerms.Add(token);
            }
        }

        return (chips, string.Join(" ", freeTerms));
    }

    /// <summary>
    /// Combines free text and active filter chips into a single query string for SearchQuery.Parse.
    /// </summary>
    public static string Combine(IEnumerable<SearchFilterChip> chips, string? freeText)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(freeText)) parts.Add(freeText.Trim());
        foreach (var chip in chips)
        {
            parts.Add(chip.RawSyntax);
        }
        return string.Join(" ", parts);
    }

    private static bool IsTrue(string v) => v.ToLowerInvariant() switch
    {
        "true" or "yes" or "1" => true,
        _ => false,
    };

    private static bool IsValidBool(string v) => v.ToLowerInvariant() switch
    {
        "true" or "yes" or "1" or "false" or "no" or "0" => true,
        _ => false,
    };

    private static bool IsValidDate(string v) =>
        DateTime.TryParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out _);

    private static IEnumerable<string> Tokenize(string input)
    {
        var sb = new StringBuilder();
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

    public bool Equals(SearchFilterChip? other)
    {
        if (other is null) return false;
        return Key == other.Key && Value.Equals(other.Value, StringComparison.OrdinalIgnoreCase);
    }

    public override bool Equals(object? obj) => obj is SearchFilterChip other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Key, Value.ToLowerInvariant());

    public override string ToString() => $"[{Label} x]";
}
