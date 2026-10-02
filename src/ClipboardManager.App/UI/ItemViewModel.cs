using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClipboardManager.Core.Models;
using ClipboardManager.Core.Services;

namespace ClipboardManager.App.UI;

public sealed class ItemViewModel : INotifyPropertyChanged
{
    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Courier New");
    private static readonly FontFamily Sans = new("Segoe UI Variable Text, Segoe UI");

    private readonly string _dataFolder;
    private bool _isMarked;
    private bool _revealed;
    private BitmapImage? _image;

    public ClipboardItem Item { get; }

    public ItemViewModel(ClipboardItem item, string dataFolder)
    {
        Item = item;
        _dataFolder = dataFolder;
    }

    public string Title => string.IsNullOrEmpty(Item.Title) ? "(empty)" : Item.Title;

    public string TypeLabel => Item.IsSensitive
        ? Humanize.ExpiresIn(Item.ExpiresAt, DateTimeOffset.UtcNow).Replace("expires in ", "")
        : Item.DisplayType;

    public FontFamily TitleFont => Item.Kind is ContentKind.Code or ContentKind.Sensitive ? Mono : Sans;

    public string PinGlyph => Item.IsPinned ? "" : ""; // Segoe Fluent/MDL2 "Pin"

    public bool IsImage => Item.Kind == ContentKind.Image;

    public bool IsMarked
    {
        get => _isMarked;
        set { if (_isMarked != value) { _isMarked = value; OnChanged(); OnChanged(nameof(MarkGlyph)); } }
    }

    public string MarkGlyph => _isMarked ? "" : ""; // "CheckMark"

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

    public BitmapImage? Image
    {
        get
        {
            if (!IsImage || Item.BinaryPath is null) return null;
            if (_image is not null) return _image;
            var path = Path.Combine(_dataFolder, Item.BinaryPath);
            if (!File.Exists(path)) return null;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 640; // thumbnail-sized decode keeps memory low
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            return _image = bmp;
        }
    }

    public string MetaType => Item.Kind == ContentKind.Sensitive ? "Sensitive · " + Item.Subtype : Item.DisplayType;
    public string MetaSource => Item.SourceApplication ?? "Unknown";
    public string MetaCopied
    {
        get
        {
            var ago = Humanize.Ago(Item.LastCopiedAt, DateTimeOffset.UtcNow);
            return Item.CopyCount > 1 ? $"{ago} · {Item.CopyCount}×" : ago;
        }
    }
    public string MetaExpires => Item.IsPinned ? "Pinned — never" : Humanize.ExpiresIn(Item.ExpiresAt, DateTimeOffset.UtcNow);
    public string MetaSize => Humanize.Bytes(Item.SizeBytes);

    public void Refresh()
    {
        OnChanged(nameof(PinGlyph));
        OnChanged(nameof(TypeLabel));
        OnChanged(nameof(MetaExpires));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
