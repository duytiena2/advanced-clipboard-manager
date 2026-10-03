using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClipboardManager.Core.Classification;
using ClipboardManager.Core.Models;
using ClipboardManager.Core.Services;

namespace ClipboardManager.App.UI;

public sealed class ItemViewModel : INotifyPropertyChanged
{
    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Courier New");
    private static readonly FontFamily Sans = new("Segoe UI Variable Text, Segoe UI");

    private readonly ClipboardService _svc;
    private bool _isMarked;
    private bool _revealed;
    private BitmapImage? _image;

    public ClipboardItem Item { get; }

    public ItemViewModel(ClipboardItem item, ClipboardService svc)
    {
        Item = item;
        _svc = svc;
    }

    public string Title => string.IsNullOrEmpty(Item.Title) ? "(empty)" : Item.Title;

    /// <summary>"1"…"9" for the first rows (Ctrl+1…9 pastes them), otherwise empty.</summary>
    public string Shortcut { get; init; } = "";

    public string ClassificationIcon => Item.ClassificationIcon;
    public string ClassificationLabel => Item.ClassificationLabel;
    public string ClassificationHeader => Item.ClassificationHeader;
    public string TimeAgo => Item.TimeAgo;
    public string TimeOrExpiry => Item.IsSensitive
        ? Humanize.ExpiresIn(Item.ExpiresAt, DateTimeOffset.UtcNow).Replace("expires in ", "exp ")
        : Item.TimeAgo;

    /// <summary>Visual Type Icon on leading edge (e.g. 🖼 for Image, T for plain text, 🗄 for SQL, {} for code/json, 🌐 for url).</summary>
    public string TypeIcon => Item.Kind switch
    {
        ContentKind.Image => "\uEB9F",
        ContentKind.Text => Item.Subtype.Equals("markdown", StringComparison.OrdinalIgnoreCase) ? "\uE8A5" : "T",
        ContentKind.Code => Item.Subtype.ToLowerInvariant() switch
        {
            "sql" => "\uE71D",
            "shell" => "\uE756",
            _ => "\uE943",
        },
        ContentKind.Url => "\uE71B",
        ContentKind.Sensitive => "\uE72E",
        ContentKind.Email => "\uE715",
        ContentKind.Phone => "\uE717",
        ContentKind.Number => "\uE8EF",
        ContentKind.Files => "\uE8B7",
        ContentKind.Snippet => "\uE8C8",
        _ => "\uE8A5",
    };

    /// <summary>Type badge text displayed on top-right (e.g. PNG, Plain, SQL, JSON, URL).</summary>
    public string TypeBadge
    {
        get
        {
            if (Item.Kind == ContentKind.Image)
            {
                var (_, _, fmt) = GetImageInfo();
                return string.IsNullOrEmpty(fmt) ? "PNG" : fmt.ToUpperInvariant();
            }
            if (Item.Kind == ContentKind.Code)
            {
                var sub = Item.Subtype.ToUpperInvariant();
                return sub switch
                {
                    "SQL" => "SQL",
                    "JSON" => "JSON",
                    "XML" => "XML",
                    "YAML" => "YAML",
                    "SHELL" => "Shell",
                    _ => string.IsNullOrEmpty(Item.ClassificationLabel) ? "Code" : Item.ClassificationLabel,
                };
            }
            if (Item.Kind == ContentKind.Url) return "URL";
            if (Item.Kind == ContentKind.Text)
            {
                if (Item.Subtype.Equals("markdown", StringComparison.OrdinalIgnoreCase)) return "MD";
                if (Item.Subtype.Equals("log", StringComparison.OrdinalIgnoreCase)) return "Log";
                return "Plain";
            }
            if (Item.Kind == ContentKind.Sensitive) return "Secret";
            if (Item.Kind == ContentKind.Snippet) return "Snippet";
            if (Item.Kind == ContentKind.Files) return "Files";
            return Item.DisplayType;
        }
    }

    public bool HasCopyCount => Item.CopyCount > 1;
    public string CopyCountText => Item.CopyCount > 1 ? $"{Item.CopyCount}×" : "";

    public bool HasPin => Item.IsPinned;
    public bool HasExpiration => !Item.IsPinned && Item.ExpiresAt != null;
    public string ExpirationBadgeText
    {
        get
        {
            if (Item.IsPinned || Item.ExpiresAt is null) return "";
            var remaining = Item.ExpiresAt.Value - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero) return "expired";
            if (remaining < TimeSpan.FromHours(1))
            {
                return $"⏱ {(int)remaining.TotalMinutes:D2}:{remaining.Seconds:D2}";
            }
            if (remaining < TimeSpan.FromDays(1))
            {
                return $"⏱ {(int)remaining.TotalHours}h";
            }
            return $"⏱ {(int)remaining.TotalDays}d";
        }
    }

    public System.Windows.Visibility ImageThumbnailVisibility => IsImage ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    public string TypeLabel => Item.IsSensitive
        ? Humanize.ExpiresIn(Item.ExpiresAt, DateTimeOffset.UtcNow).Replace("expires in ", "")
        : Item.DisplayType;

    public FontFamily TitleFont => Item.Kind is ContentKind.Code or ContentKind.Sensitive ? Mono : Sans;

    public string PinGlyph => Item.IsPinned ? "" : ""; // Segoe Fluent/MDL2 "Pin"

    public bool IsImage => Item.Kind == ContentKind.Image;

    public bool IsMarked
    {
        get => _isMarked;
        set
        {
            if (_isMarked != value)
            {
                _isMarked = value;
                OnChanged();
                OnChanged(nameof(MarkGlyph));
                OnChanged(nameof(CheckboxGlyph));
                OnChanged(nameof(MarkOrderText));
            }
        }
    }

    public string MarkGlyph => _isMarked ? "" : ""; // "CheckMark"
    public string CheckboxGlyph => _isMarked ? "\uE73E" : "\uE739"; // Checked or Unchecked box
    public string MarkOrderText => _isMarked && MarkOrder > 0 ? $"{MarkOrder}" : "";
    public string SensitiveGlyph => Item.IsSensitive ? "\uE72E" : ""; // Lock
    public bool HasSensitiveBadge => Item.IsSensitive;

    /// <summary>When the item was marked (a sequence number): the paste stack uses marking order.</summary>
    public long MarkOrder { get; set; }

    public bool Revealed
    {
        get => _revealed;
        set { _revealed = value; OnChanged(nameof(PreviewText)); }
    }

    public string PreviewText
    {
        get
        {
            if (Item.Kind == ContentKind.Image) return "";
            if (Item.IsSensitive && !_revealed) return Item.Title + "\n\nSensitive content is hidden. Press Ctrl+R to reveal.";
            var t = Item.TextContent ?? "";
            return t.Length > 20_000 ? t[..20_000] + "\n…" : t;
        }
    }

    public FontFamily PreviewFont => Item.Kind is ContentKind.Text or ContentKind.Email or ContentKind.Url or ContentKind.Phone ? Sans : Mono;

    public bool IsSensitive => Item.IsSensitive;
    public bool IsCode => Item.Kind == ContentKind.Code || Item.Subtype is "sql" or "json" or "xml" or "yaml" or "shell";
    public bool IsUrl => Item.Kind == ContentKind.Url;
    public bool IsFiles => Item.Kind == ContentKind.Files;
    public bool IsSnippet => Item.Kind == ContentKind.Snippet;

    public string CodeLanguage => Item.Kind == ContentKind.Code ? Item.ClassificationLabel : Item.DisplayType;
    public int TextLength => (Item.TextContent ?? "").Length;
    public int LineCount => string.IsNullOrEmpty(Item.TextContent) ? 0 : Item.TextContent.Split('\n').Length;
    public string CodeStats => $"{CodeLanguage} · {TextLength} characters";

    public (int Width, int Height, string Format) GetImageInfo()
    {
        int width = 0;
        int height = 0;
        string format = "PNG";
        if (!string.IsNullOrEmpty(Item.MetadataJson))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(Item.MetadataJson);
                if (doc.RootElement.TryGetProperty("width", out var w)) width = w.GetInt32();
                if (doc.RootElement.TryGetProperty("height", out var h)) height = h.GetInt32();
                if (doc.RootElement.TryGetProperty("mime", out var m))
                {
                    var mime = m.GetString();
                    if (mime?.Contains("jpeg") == true || mime?.Contains("jpg") == true) format = "JPEG";
                    else if (mime?.Contains("png") == true) format = "PNG";
                    else if (mime?.Contains("bmp") == true) format = "BMP";
                    else if (mime?.Contains("gif") == true) format = "GIF";
                }
            }
            catch { }
        }
        if (width == 0 || height == 0)
        {
            if (Item.Title.StartsWith("Image ") && Item.Title.Contains('×'))
            {
                var parts = Item.Title.Replace("Image ", "").Split('×');
                if (parts.Length == 2 && int.TryParse(parts[0].Trim(), out var pw) && int.TryParse(parts[1].Trim(), out var ph))
                {
                    width = pw;
                    height = ph;
                }
            }
            if (width == 0 && Image is { } bmp && bmp.PixelWidth > 0 && bmp.PixelHeight > 0)
            {
                width = bmp.PixelWidth;
                height = bmp.PixelHeight;
            }
        }
        return (width, height, format);
    }

    public string ImageResolutionText
    {
        get
        {
            var (w, h, fmt) = GetImageInfo();
            return w > 0 && h > 0 ? $"{fmt} · {w} × {h}" : fmt;
        }
    }

    public bool HasOcr => !string.IsNullOrWhiteSpace(Item.OcrText);
    public string OcrStatusText => HasOcr ? "OCR: Available" : (Item.OcrText == "" ? "OCR: None detected" : "OCR: None");

    public string SecretTypeName => Item.ClassificationLabel;

    public string MaskedSecret => ClipboardManager.Core.Classification.ContentClassifier.Mask(Item.TextContent ?? "");

    public string UrlDomain
    {
        get
        {
            if (!string.IsNullOrEmpty(Item.TextContent) && ClipboardManager.Core.Classification.ContentClassifier.TryGetHost(Item.TextContent.Trim()) is { } host)
                return host;
            return "Link";
        }
    }

    public string UrlTitle
    {
        get
        {
            var host = UrlDomain.ToLowerInvariant();
            if (host.EndsWith("github.com")) return "GitHub";
            if (host.Contains("youtube.com") || host.EndsWith("youtu.be")) return "YouTube";
            if (host.Contains("figma.com")) return "Figma";
            if (host.Contains("gitlab.com")) return "GitLab";
            if (host.Contains("stackoverflow.com")) return "Stack Overflow";
            if (host.Contains("google.com")) return "Google";
            if (host.Contains("microsoft.com")) return "Microsoft";
            if (host.Contains("twitter.com") || host.Contains("x.com")) return "X / Twitter";
            return host;
        }
    }

    public BitmapImage? Image
    {
        get
        {
            if (!IsImage || Item.BinaryPath is null) return null;
            if (_image is not null) return _image;
            byte[]? png;
            try { png = _svc.ReadBinary(Item); } // decrypts when the history is encrypted
            catch (IOException) { return null; }
            catch (InvalidOperationException) { return null; }
            if (png is null) return null;
            using var ms = new MemoryStream(png);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 640; // thumbnail-sized decode keeps memory low
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return _image = bmp;
        }
    }

    public string SectionName => Item.IsPinned ? "PINNED" : "RECENT";
    public string SectionHeader => Item.IsPinned ? "📌 PINNED" : "RECENT";

    public bool IsMultiSelectMode
    {
        get => _svc.Count() > 0 && (_isMarked || false); // Or driven by parent
        set { OnChanged(nameof(SelectGlyph)); }
    }

    private bool _multiSelectActive;
    public bool MultiSelectActive
    {
        get => _multiSelectActive;
        set { if (_multiSelectActive != value) { _multiSelectActive = value; OnChanged(); OnChanged(nameof(SelectGlyph)); } }
    }

    public string SelectGlyph => _isMarked ? "☑" : (_multiSelectActive ? "☐" : "");

    public string PinIcon => Item.IsPinned ? "📌" : "";
    public string SensitiveIcon => Item.IsSensitive ? "🔒" : "";

    public bool IsExpiringSoon
    {
        get
        {
            if (Item.IsPinned || Item.ExpiresAt is null) return false;
            var remaining = Item.ExpiresAt.Value - DateTimeOffset.UtcNow;
            return remaining > TimeSpan.Zero && remaining <= TimeSpan.FromMinutes(5);
        }
    }

    public string ExpirationDisplay
    {
        get
        {
            if (Item.IsPinned) return "📌 Never expires";
            if (Item.ExpiresAt is null) return "Never expires";
            var remaining = Item.ExpiresAt.Value - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero) return "Expired";
            if (remaining < TimeSpan.FromHours(1))
            {
                string timeStr = $"{(int)remaining.TotalMinutes:D2}:{remaining.Seconds:D2}";
                return remaining < TimeSpan.FromMinutes(5) ? $"⚠ {timeStr}" : $"⏱ {timeStr}";
            }
            if (remaining < TimeSpan.FromDays(1))
            {
                return $"⏱ {(int)remaining.TotalHours}h {remaining.Minutes}m";
            }
            return $"⏱ {(int)remaining.TotalDays}d";
        }
    }

    public string ExpirationTooltip
    {
        get
        {
            if (Item.IsPinned) return "📌 Never expires";
            if (Item.ExpiresAt is null) return "Never expires";
            var remaining = Item.ExpiresAt.Value - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero) return "Expired";
            var ret = _svc.Expiration.RetentionFor(Item.Kind, Item.Subtype);
            string retStr = ret is not null ? Humanize.Retention(ret.Value) : "custom";
            return $"Expires in {Humanize.ExpiresIn(Item.ExpiresAt, DateTimeOffset.UtcNow).Replace("expires in ", "")}\nRetention: {Item.DisplayType} · {retStr}";
        }
    }

    public string HoverToolTip
    {
        get
        {
            string snippet = !string.IsNullOrEmpty(Item.TextContent)
                ? (Item.TextContent.Length > 250 ? Item.TextContent[..250].Trim() + "…" : Item.TextContent.Trim())
                : Item.Title;
            int charCount = Item.TextContent?.Length ?? 0;
            string sizeInfo = charCount > 0 ? $"{charCount} chars" : MetaSize;
            return $"{snippet}\n\n─────────────────────\n{ClassificationLabel} · {sizeInfo} · {MetaCopied}\n{ExpirationDisplay}";
        }
    }

    public string MetaType => Item.Kind == ContentKind.Sensitive ? $"🔒 Sensitive · {Item.ClassificationLabel}"
        : Item.HasRichText ? $"{Item.ClassificationHeader} · formatted"
        : Item.OcrText is { Length: > 0 } ? $"{Item.ClassificationHeader} · contains text" : Item.ClassificationHeader;

    public string MetaSource
    {
        get
        {
            string source = Item.Kind == ContentKind.Snippet ? "Snippet" : (Item.SourceApplication ?? "Unknown");
            bool isAuto = !string.IsNullOrEmpty(Item.SourceApplication) &&
                          _svc.WorkspaceFor(Item.SourceApplication).Equals(Item.Workspace, StringComparison.OrdinalIgnoreCase);
            string autoTag = isAuto ? " · Auto" : "";
            return $"{source} → {Item.Workspace}{autoTag}";
        }
    }

    public string MetaCopied
    {
        get
        {
            var ago = Humanize.Ago(Item.LastCopiedAt, DateTimeOffset.UtcNow);
            return Item.CopyCount > 1 ? $"{ago} · {Item.CopyCount}×" : ago;
        }
    }
    public string MetaExpires => Item.IsPinned ? "📌 Never expires" : ExpirationDisplay;
    public string MetaSize => Humanize.Bytes(Item.SizeBytes);

    public void Refresh()
    {
        OnChanged(nameof(PinGlyph));
        OnChanged(nameof(PinIcon));
        OnChanged(nameof(HasPin));
        OnChanged(nameof(SensitiveIcon));
        OnChanged(nameof(HasSensitiveBadge));
        OnChanged(nameof(TypeLabel));
        OnChanged(nameof(TypeBadge));
        OnChanged(nameof(TypeIcon));
        OnChanged(nameof(TimeAgo));
        OnChanged(nameof(TimeOrExpiry));
        OnChanged(nameof(MetaExpires));
        OnChanged(nameof(MetaCopied));
        OnChanged(nameof(ExpirationDisplay));
        OnChanged(nameof(ExpirationTooltip));
        OnChanged(nameof(ExpirationBadgeText));
        OnChanged(nameof(HasExpiration));
        OnChanged(nameof(HasCopyCount));
        OnChanged(nameof(CopyCountText));
        OnChanged(nameof(IsExpiringSoon));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
