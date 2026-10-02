namespace ClipboardManager.Core.Models;

/// <summary>Top-level kind of a clipboard item.</summary>
public enum ContentKind
{
    Text,
    Code,
    Url,
    Email,
    Phone,
    Number,
    Image,
    Files,
    Sensitive,
}

/// <summary>A single stored clipboard entry.</summary>
public sealed class ClipboardItem
{
    public long Id { get; set; }
    public ContentKind Kind { get; set; }
    /// <summary>Finer type: sql, json, shell, github, api-key, png …</summary>
    public string Subtype { get; set; } = "";
    public string Title { get; set; } = "";
    public string? TextContent { get; set; }
    /// <summary>Relative path (inside the data folder) of a binary payload, e.g. an image.</summary>
    public string? BinaryPath { get; set; }
    public string ContentHash { get; set; } = "";
    public long SizeBytes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastCopiedAt { get; set; }
    public DateTimeOffset? AccessedAt { get; set; }
    /// <summary>Null = never expires.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }
    public bool IsPinned { get; set; }
    public bool IsSensitive { get; set; }
    public int CopyCount { get; set; } = 1;
    public string Workspace { get; set; } = "Default";
    public string? SourceApplication { get; set; }
    public double Confidence { get; set; }
    public string? MetadataJson { get; set; }

    public string DisplayType => string.IsNullOrEmpty(Subtype) ? Kind.ToString() : Subtype.ToUpperInvariant() switch
    {
        "SQL" => "SQL",
        "JSON" => "JSON",
        "XML" => "XML",
        "YAML" => "YAML",
        "SHELL" => "Shell",
        "MARKDOWN" => "Markdown",
        _ => Kind == ContentKind.Sensitive ? "Sensitive" : char.ToUpperInvariant(Subtype[0]) + Subtype[1..],
    };
}

/// <summary>Raw content as read from the OS clipboard.</summary>
public sealed class CapturedContent
{
    public string? Text { get; init; }
    public byte[]? ImagePng { get; init; }
    public int ImageWidth { get; init; }
    public int ImageHeight { get; init; }
    public IReadOnlyList<string>? Files { get; init; }
    public string? SourceApplication { get; init; }

    public static CapturedContent FromText(string text, string? source = null) => new() { Text = text, SourceApplication = source };
    public static CapturedContent FromFiles(IReadOnlyList<string> files, string? source = null) => new() { Files = files, SourceApplication = source };
    public static CapturedContent FromImage(byte[] png, int w, int h, string? source = null) =>
        new() { ImagePng = png, ImageWidth = w, ImageHeight = h, SourceApplication = source };
}

public sealed record ClassificationResult(ContentKind Kind, string Subtype, double Confidence, bool IsSensitive = false);
