using ClipboardManager.Core.Classification;
using ClipboardManager.Core.Services;

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
    /// <summary>User-made reusable text/template; never expires and is never cleared or evicted.</summary>
    Snippet,
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
    /// <summary>True when HTML or RTF formatting was captured with the text (loaded on demand).</summary>
    public bool HasRichText { get; set; }
    /// <summary>Text recognized in an image. Null = not processed yet, "" = no text found.</summary>
    public string? OcrText { get; set; }

    public string ClassificationIcon => ContentClassifier.GetClassificationIcon(Kind, Subtype, TextContent);
    public string ClassificationLabel => ContentClassifier.GetClassificationLabel(Kind, Subtype, TextContent);
    public string ClassificationHeader => $"{ClassificationIcon} {ClassificationLabel}";
    public string TimeAgo => Humanize.Ago(LastCopiedAt, DateTimeOffset.UtcNow);

    public string DisplayType => ClassificationLabel;

    public string TypeIcon => Kind switch
    {
        ContentKind.Image => "🖼",
        ContentKind.Text => Subtype.Equals("markdown", StringComparison.OrdinalIgnoreCase) ? "MD" : "T",
        ContentKind.Code => Subtype.ToUpperInvariant() switch
        {
            "SQL" => "🗄",
            "SHELL" => "⌨",
            _ => "{;}",
        },
        ContentKind.Url => "🌐",
        ContentKind.Sensitive => "🔒",
        ContentKind.Email => "✉",
        ContentKind.Phone => "📞",
        ContentKind.Files => "📁",
        ContentKind.Snippet => "📋",
        _ => "T",
    };

    public string TypeBadge => Kind switch
    {
        ContentKind.Image => string.IsNullOrEmpty(Subtype) ? "PNG" : Subtype.ToUpperInvariant(),
        ContentKind.Code => Subtype.ToUpperInvariant() switch
        {
            "SQL" => "SQL",
            "JSON" => "JSON",
            "XML" => "XML",
            "YAML" => "YAML",
            "SHELL" => "Shell",
            _ => ClassificationLabel,
        },
        ContentKind.Url => "URL",
        ContentKind.Text => Subtype.Equals("markdown", StringComparison.OrdinalIgnoreCase) ? "MD" : "Plain",
        ContentKind.Sensitive => "Secret",
        ContentKind.Snippet => "Snippet",
        ContentKind.Files => "Files",
        _ => DisplayType,
    };

    public bool HasCopyCount => CopyCount > 1;
    public string CopyCountText => CopyCount > 1 ? $"{CopyCount}×" : "";
    public string PinGlyph => IsPinned ? "📌" : "";
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
    /// <summary>Formatting that came with <see cref="Text"/> (raw CF_HTML / RTF), restored on a normal paste.</summary>
    public string? Html { get; init; }
    public string? Rtf { get; init; }

    public static CapturedContent FromText(string text, string? source = null) => new() { Text = text, SourceApplication = source };
    public static CapturedContent FromFiles(IReadOnlyList<string> files, string? source = null) => new() { Files = files, SourceApplication = source };
    public static CapturedContent FromImage(byte[] png, int w, int h, string? source = null) =>
        new() { ImagePng = png, ImageWidth = w, ImageHeight = h, SourceApplication = source };
}

/// <summary>Formatted versions of a text item.</summary>
public sealed record RichText(string? Html, string? Rtf)
{
    public bool IsEmpty => string.IsNullOrEmpty(Html) && string.IsNullOrEmpty(Rtf);
}

/// <summary>Everything the OS clipboard writer needs to put an item back on the clipboard.</summary>
public sealed class ClipboardPayload
{
    public string? Text { get; init; }
    public string? Html { get; init; }
    public string? Rtf { get; init; }
    public byte[]? ImagePng { get; init; }
    public IReadOnlyList<string>? Files { get; init; }
    /// <summary>Asks the OS to keep the value out of its own clipboard history and cloud clipboard.</summary>
    public bool IsSensitive { get; init; }

    public bool IsEmpty => string.IsNullOrEmpty(Text) && ImagePng is not { Length: > 0 } && Files is not { Count: > 0 };
}

public sealed record ClassificationResult(ContentKind Kind, string Subtype, double Confidence, bool IsSensitive = false);
