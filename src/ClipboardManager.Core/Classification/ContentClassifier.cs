using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using ClipboardManager.Core.Models;

namespace ClipboardManager.Core.Classification;

/// <summary>
/// Local, rule-based, confidence-scored classifier. Order: sensitive → structured → links → code → text.
/// </summary>
public sealed class ContentClassifier
{
    private const RegexOptions Opts = RegexOptions.Compiled | RegexOptions.CultureInvariant;

    private static readonly Regex UrlRx = new(@"^(?:https?|ftp)://[^\s/$.?#][^\s]*$", Opts | RegexOptions.IgnoreCase);
    private static readonly Regex WwwRx = new(@"^www\.[a-z0-9-]+(?:\.[a-z0-9-]+)+(?:/\S*)?$", Opts | RegexOptions.IgnoreCase);
    private static readonly Regex EmailRx = new(@"^[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}$", Opts);
    private static readonly Regex PhoneRx = new(@"^\+?[0-9][0-9 ().-]{7,18}[0-9]$", Opts);
    private static readonly Regex NumberRx = new(@"^[-+]?(?:\d{1,3}(?:[,.]\d{3})+|\d+)(?:[.,]\d+)?$", Opts);
    private static readonly Regex Ipv4Rx = new(@"^(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)(?::\d{1,5})?$", Opts);
    private static readonly Regex SqlRx = new(
        @"^\s*(?:SELECT\s.+\sFROM\s|INSERT\s+INTO\s|UPDATE\s+\S+\s+SET\s|DELETE\s+FROM\s|CREATE\s+(?:TABLE|INDEX|VIEW|VIRTUAL\s+TABLE|DATABASE|PROCEDURE|FUNCTION)\s|ALTER\s+TABLE\s|DROP\s+(?:TABLE|INDEX|VIEW|DATABASE)\s|WITH\s+\w+\s+AS\s*\()",
        Opts | RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex ShellRx = new(
        @"^\s*(?:\$\s+)?(?:sudo\s+)?(?:git|docker|docker-compose|kubectl|npm|npx|pnpm|yarn|pip|pip3|python|python3|dotnet|cargo|go|ssh|scp|curl|wget|cd|ls|mkdir|rm|cp|mv|chmod|chown|cat|grep|tail|head|systemctl|apt|apt-get|brew|winget|choco|helm|terraform|az|aws|gcloud|ping|tracert|traceroute|netstat|ipconfig|powershell|pwsh|Get-\w+|Set-\w+)(?:\s|$)",
        Opts);
    private static readonly Regex MarkdownRx = new(@"(?m)^(?:#{1,6}\s+\S|[-*+]\s+\S|\d+\.\s+\S|>\s+\S|```)|\[[^\]]+\]\([^)]+\)|\*\*[^*]+\*\*", Opts);
    private static readonly Regex YamlLineRx = new(@"^\s*(?:-\s+)?[A-Za-z_][\w.-]*:(?:\s+\S.*)?$", Opts);
    private static readonly Regex LogLineRx = new(@"^\s*(?:\[?\d{4}-\d{2}-\d{2}[ T]\d{2}:\d{2}:\d{2}|\[?(?:INFO|WARN|WARNING|ERROR|DEBUG|TRACE|FATAL)\]?\s)", Opts);
    private static readonly Regex CodeTokenRx = new(
        @"\b(?:function|const|let|var|return|class|public|private|static|void|import|from|def|fn|func|package|namespace|using|interface|async|await|=>|#include|println|console\.log|System\.out)\b|[{};]\s*$|=>",
        Opts | RegexOptions.Multiline);

    public ClassificationResult Classify(CapturedContent content)
    {
        if (content.ImagePng is { Length: > 0 }) return new(ContentKind.Image, "png", 1.0);
        if (content.Files is { Count: > 0 }) return new(ContentKind.Files, content.Files.Count == 1 ? "file" : "files", 1.0);
        return ClassifyText(content.Text ?? "");
    }

    public ClassificationResult ClassifyText(string text)
    {
        var t = text.Trim();
        if (t.Length == 0) return new(ContentKind.Text, "plain", 0.5);

        var secret = SensitiveDataDetector.Detect(t);
        if (secret is not null) return new(ContentKind.Sensitive, secret, 0.9, IsSensitive: true);

        bool singleLine = !t.Contains('\n');

        if (singleLine)
        {
            if (UrlRx.IsMatch(t) || WwwRx.IsMatch(t)) return new(ContentKind.Url, UrlSubtype(t), 0.98);
            if (EmailRx.IsMatch(t)) return new(ContentKind.Email, "email", 0.97);
            if (Ipv4Rx.IsMatch(t)) return new(ContentKind.Text, "ip", 0.95);
            if (NumberRx.IsMatch(t)) return new(ContentKind.Number, "number", 0.95);
            if (PhoneRx.IsMatch(t) && t.Count(char.IsDigit) >= 9) return new(ContentKind.Phone, "phone", 0.85);
        }

        if (LooksLikeJson(t)) return new(ContentKind.Code, "json", 0.98);
        if (LooksLikeXml(t)) return new(ContentKind.Code, "xml", 0.95);
        if (SqlRx.IsMatch(t)) return new(ContentKind.Code, "sql", 0.95);
        if (ShellRx.IsMatch(FirstLine(t))) return new(ContentKind.Code, "shell", 0.9);
        if (LooksLikeLog(t)) return new(ContentKind.Text, "log", 0.85);
        if (LooksLikeYaml(t)) return new(ContentKind.Code, "yaml", 0.8);

        int codeHits = CodeTokenRx.Matches(t).Count;
        int lines = t.Split('\n').Length;
        if (codeHits >= 2 && (codeHits >= lines / 3.0 || lines <= 3)) return new(ContentKind.Code, "code", Math.Min(0.6 + codeHits * 0.05, 0.9));

        if (!singleLine && MarkdownRx.Matches(t).Count >= 2) return new(ContentKind.Text, "markdown", 0.75);

        return new(ContentKind.Text, "plain", 0.6);
    }

    private static string FirstLine(string t)
    {
        int i = t.IndexOf('\n');
        return i < 0 ? t : t[..i];
    }

    private static string UrlSubtype(string url)
    {
        var host = TryGetHost(url);
        if (host is null) return "url";
        if (host.EndsWith("github.com", StringComparison.OrdinalIgnoreCase)) return "github";
        if (host.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) || host.EndsWith("youtu.be", StringComparison.OrdinalIgnoreCase)) return "youtube";
        if (host.Contains("figma.com", StringComparison.OrdinalIgnoreCase)) return "figma";
        return "url";
    }

    public static string? TryGetHost(string url)
    {
        var candidate = url.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? "https://" + url : url;
        return Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ? uri.Host : null;
    }

    private static bool LooksLikeJson(string t)
    {
        if (!((t.StartsWith('{') && t.EndsWith('}')) || (t.StartsWith('[') && t.EndsWith(']')))) return false;
        try
        {
            using var _ = JsonDocument.Parse(t);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool LooksLikeXml(string t)
    {
        if (!(t.StartsWith('<') && t.EndsWith('>'))) return false;
        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using var reader = XmlReader.Create(new StringReader(t), settings);
            while (reader.Read()) { }
            return true;
        }
        catch (XmlException)
        {
            return false;
        }
    }

    private static bool LooksLikeYaml(string t)
    {
        var lines = t.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith('#')).ToList();
        if (lines.Count < 2) return false;
        int hits = lines.Count(l => YamlLineRx.IsMatch(l) || l.TrimStart().StartsWith("- "));
        bool hasNesting = lines.Any(l => l.StartsWith("  ") || l.StartsWith("\t"));
        return hits >= lines.Count * 0.8 && (hasNesting || lines.Count >= 3);
    }

    private static bool LooksLikeLog(string t)
    {
        var lines = t.Split('\n').Where(l => l.Trim().Length > 0).ToList();
        if (lines.Count == 0) return false;
        int hits = lines.Count(l => LogLineRx.IsMatch(l));
        return hits >= Math.Max(1, lines.Count * 0.6);
    }

    /// <summary>Builds a short one-line title for list display.</summary>
    public static string MakeTitle(string? text, int max = 120)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var line = text.Trim().Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
        line = Regex.Replace(line, @"\s+", " ");
        return line.Length <= max ? line : line[..(max - 1)] + "…";
    }

    /// <summary>Masked preview for sensitive values, e.g. "sk_live_••••••••ab12".</summary>
    public static string Mask(string secret)
    {
        var s = secret.Trim();
        if (s.Length <= 8) return new string('•', s.Length);
        int keepStart = Math.Min(8, s.Length / 4);
        return s[..keepStart] + new string('•', 12) + s[^4..];
    }
}
