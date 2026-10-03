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
    private static readonly Regex JsRx = new(
        @"(?:\b(?:const|let|var)\s+[A-Za-z_$]\w*\s*=|=>|\bfunction\s*[A-Za-z_$]?\w*\s*\(|\bconsole\.(?:log|error|warn|info)\b|\b(?:async\s+)?function\b|\bimport\s+.*?\s+from\s+['""]|\bexport\s+(?:default|const|let|var|function|class)\b|\btypeof\s+\w+|\bdocument\.(?:getElementById|querySelector)\b|\bprocess\.env\b|\bJSON\.(?:parse|stringify)\b)",
        Opts);
    private static readonly Regex PythonRx = new(
        @"(?m)^\s*(?:def\s+[A-Za-z_]\w*\s*\(.*?\)\s*:|class\s+[A-Za-z_]\w*.*?:|import\s+[A-Za-z_]|from\s+[A-Za-z_]\w*\s+import|\bprint\s*\(|\belif\b|\b__init__\b|\bself\.[A-Za-z_])",
        Opts);
    private static readonly Regex CSharpRx = new(
        @"(?m)\b(?:using\s+System(?:\.\w+)*\s*;|namespace\s+[A-Za-z_]|public\s+(?:class|record|struct|interface|enum)\s+[A-Za-z_]|Console\.WriteLine\b|Task<(?:[A-Za-z_]|void)>|async\s+Task\b|var\s+[A-Za-z_]\w*\s*=\s*new\s+[A-Za-z_])",
        Opts);
    private static readonly Regex HtmlRx = new(
        @"<!DOCTYPE\s+html|<html[\s>]|<div[\s>]|<span[\s>]|<body[\s>]|<table[\s>]|<script[\s>]|<style[\s>]",
        Opts | RegexOptions.IgnoreCase);
    private static readonly Regex CssRx = new(
        @"(?m)^\s*[.#]?[a-zA-Z_-][\w-]*\s*\{[^}]*?(?:display|color|margin|padding|background|font-size|border)\s*:[^}]+}",
        Opts);
    private static readonly Regex GoRx = new(
        @"\b(?:func\s+(?:\([A-Za-z_]\w*\s+\*?[A-Za-z_]\w*\)\s+)?[A-Za-z_]\w*\(|package\s+(?:main|[a-z_]\w*)\b|fmt\.(?:Println|Printf|Sprintf)\b)",
        Opts);
    private static readonly Regex RustRx = new(
        @"\b(?:fn\s+[a-z_]\w*\s*\(|let\s+mut\s+[a-z_]|impl\s+[A-Za-z_]|println!\s*\()",
        Opts);
    private static readonly Regex JavaRx = new(
        @"\b(?:public\s+static\s+void\s+main\s*\(|System\.out\.println\s*\()",
        Opts);

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

    /// <summary>Infers code language (JavaScript, Python, C#, etc.) from text when subtype is generic code.</summary>
    public static string DetectCodeLanguage(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "Code";
        var t = text.Trim();
        if (JsRx.IsMatch(t)) return "JavaScript";
        if (PythonRx.IsMatch(t)) return "Python";
        if (CSharpRx.IsMatch(t)) return "C#";
        if (HtmlRx.IsMatch(t)) return "HTML";
        if (CssRx.IsMatch(t)) return "CSS";
        if (GoRx.IsMatch(t)) return "Go";
        if (RustRx.IsMatch(t)) return "Rust";
        if (JavaRx.IsMatch(t)) return "Java";
        return "Code";
    }

    /// <summary>Returns a friendly human-readable classification label (e.g. "SQL", "JavaScript", "GitHub URL", "Screenshot", "JSON").</summary>
    public static string GetClassificationLabel(ContentKind kind, string subtype, string? text = null)
    {
        return kind switch
        {
            ContentKind.Code => subtype.ToLowerInvariant() switch
            {
                "sql" => "SQL",
                "json" => "JSON",
                "xml" => "XML",
                "yaml" => "YAML",
                "shell" => "Shell",
                _ => DetectCodeLanguage(text),
            },
            ContentKind.Url => subtype.ToLowerInvariant() switch
            {
                "github" => "GitHub URL",
                "youtube" => "YouTube URL",
                "figma" => "Figma URL",
                _ => "URL",
            },
            ContentKind.Image => "Screenshot",
            ContentKind.Text => subtype.ToLowerInvariant() switch
            {
                "markdown" => "Markdown",
                "log" => "Log",
                "ip" => "IP Address",
                _ => "Plain Text",
            },
            ContentKind.Email => "Email",
            ContentKind.Phone => "Phone",
            ContentKind.Number => "Number",
            ContentKind.Files => subtype.Equals("file", StringComparison.OrdinalIgnoreCase) ? "File" : "Files",
            ContentKind.Snippet => subtype.Equals("template", StringComparison.OrdinalIgnoreCase) ? "Template" : "Snippet",
            ContentKind.Sensitive => subtype.ToLowerInvariant() switch
            {
                "api-key" => "API Key",
                "aws-key" => "AWS Key",
                "token" => "Token",
                "jwt" => "JWT",
                "private-key" => "Private Key",
                "auth-header" => "Auth Header",
                "connection-string" => "Connection String",
                "password" => "Password",
                _ => "Sensitive",
            },
            _ => !string.IsNullOrEmpty(subtype) ? char.ToUpperInvariant(subtype[0]) + subtype[1..] : kind.ToString(),
        };
    }

    /// <summary>
    /// Returns a Segoe Fluent Icons / MDL2 Assets Unicode codepoint for the classification.
    /// These are monochrome vector glyphs — no color emoji — they inherit Foreground and
    /// automatically invert (black → white) when a ListBoxItem is selected.
    /// Reference: https://docs.microsoft.com/en-us/windows/apps/design/style/segoe-fluent-icons-font
    /// </summary>
    public static string GetClassificationIcon(ContentKind kind, string subtype, string? text = null)
    {
        // All values are Segoe Fluent Icons / Segoe MDL2 Assets codepoints.
        // Render with FontFamily="Segoe Fluent Icons, Segoe MDL2 Assets" in XAML.
        return kind switch
        {
            ContentKind.Code => subtype.ToLowerInvariant() switch
            {
                "sql"          => "\uE71D",  // StorageOptical (database cylinder look)
                "json"         => "\uE943",  // Code (curly braces glyph)
                "xml"          => "\uE943",  // Code
                "yaml"         => "\uE8A5",  // Document (structured text)
                "shell"        => "\uE756",  // CommandPrompt
                _              => "\uE943",  // Code (generic)
            },
            ContentKind.Url => subtype.ToLowerInvariant() switch
            {
                "github"       => "\uE71B",  // Link (chain link)
                "youtube"      => "\uE714",  // Video
                "figma"        => "\uE771",  // Design / Shapes
                _              => "\uE71B",  // Link
            },
            ContentKind.Image  => "\uEB9F",  // Photo (camera/image glyph)
            ContentKind.Text   => subtype.ToLowerInvariant() switch
            {
                "markdown"     => "\uE8A5",  // Document
                "log"          => "\uE9D9",  // ClipboardList / Activity
                "ip"           => "\uE839",  // Globe / Network
                _              => "\uE8A5",  // Document (plain text)
            },
            ContentKind.Email      => "\uE715",  // Mail
            ContentKind.Phone      => "\uE717",  // Phone
            ContentKind.Number     => "\uE8EF",  // Calculator
            ContentKind.Files      => "\uE8B7",  // Folder
            ContentKind.Snippet    => "\uE8C8",  // Paste / Snippet
            ContentKind.Sensitive  => "\uE72E",  // Lock (padlock)
            _                      => "\uE8A5",  // Document (fallback)
        };
    }

    /// <summary>Returns the Segoe Fluent Icons codepoint concatenated with the label, e.g. "\uE71D SQL" or "\uE943 JavaScript". Render the icon portion with FontFamily="Segoe Fluent Icons, Segoe MDL2 Assets".</summary>
    public static string GetClassificationHeader(ContentKind kind, string subtype, string? text = null)
    {
        var icon = GetClassificationIcon(kind, subtype, text);
        var label = GetClassificationLabel(kind, subtype, text);
        return $"{icon} {label}";
    }
}
