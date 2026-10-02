using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClipboardManager.Core.Classification;
using ClipboardManager.Core.Models;
using ClipboardManager.Core.Platform;
using ClipboardManager.Core.Search;
using ClipboardManager.Core.Storage;

namespace ClipboardManager.Core.Services;

public enum CaptureOutcome
{
    Stored,
    Duplicate,
    SkippedPaused,
    SkippedEmpty,
    SkippedExcludedApp,
    SkippedByPrivacyRule,
    SkippedTooLarge,
}

/// <summary>
/// Orchestrates capture → classify → expiration → store, plus search, pin, delete and cleanup.
/// Platform-independent: the OS layer only feeds <see cref="CapturedContent"/> in.
/// </summary>
public sealed class ClipboardService : IDisposable
{
    private readonly ClipboardRepository _repo;
    private readonly ContentClassifier _classifier = new();
    private readonly IClock _clock;
    private readonly string _dataFolder;

    public AppSettings Settings { get; }
    public ExpirationPolicy Expiration { get; }
    public string DataFolder => _dataFolder;
    public string ImagesFolder => Path.Combine(_dataFolder, "images");
    public bool FullTextSearchEnabled => _repo.FullTextEnabled;

    /// <summary>Raised after an item was stored or bumped (UI refresh).</summary>
    public event EventHandler<ClipboardItem>? ItemCaptured;
    public event EventHandler? HistoryChanged;

    public ClipboardService(string dataFolder, AppSettings settings, IClock? clock = null, bool enableFullText = true)
    {
        _dataFolder = dataFolder;
        Directory.CreateDirectory(dataFolder);
        Directory.CreateDirectory(ImagesFolder);
        Settings = settings;
        Expiration = new ExpirationPolicy(settings);
        _clock = clock ?? new SystemClock();
        _repo = new ClipboardRepository(Path.Combine(dataFolder, "clipboard.db"), enableFullText);
    }

    public static string DefaultDataFolder() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipboardManager");

    public (CaptureOutcome Outcome, ClipboardItem? Item) Capture(CapturedContent content)
    {
        if (!Settings.CaptureEnabled) return (CaptureOutcome.SkippedPaused, null);
        if (IsExcluded(content.SourceApplication)) return (CaptureOutcome.SkippedExcludedApp, null);

        bool hasText = !string.IsNullOrWhiteSpace(content.Text);
        bool hasImage = content.ImagePng is { Length: > 0 };
        bool hasFiles = content.Files is { Count: > 0 };
        if (!hasText && !hasImage && !hasFiles) return (CaptureOutcome.SkippedEmpty, null);
        if (hasImage && content.ImagePng!.Length > Settings.MaxImageBytes) return (CaptureOutcome.SkippedTooLarge, null);
        if (hasText && !hasFiles && !hasImage && content.Text!.Length > Settings.MaxTextChars) return (CaptureOutcome.SkippedTooLarge, null);

        var result = _classifier.Classify(content);
        if (!Settings.DetectSensitive && result.IsSensitive)
        {
            result = new ClassificationResult(ContentKind.Text, "plain", 0.5);
        }
        if (result.IsSensitive)
        {
            if (Settings.NeverStorePrivateKeys && result.Subtype == "private-key") return (CaptureOutcome.SkippedByPrivacyRule, null);
            if (Settings.NeverStorePasswords && result.Subtype == "password") return (CaptureOutcome.SkippedByPrivacyRule, null);
        }

        var now = _clock.Now;
        var item = new ClipboardItem
        {
            Kind = result.Kind,
            Subtype = result.Subtype,
            Confidence = result.Confidence,
            IsSensitive = result.IsSensitive,
            CreatedAt = now,
            LastCopiedAt = now,
            Workspace = Settings.DefaultWorkspace,
            SourceApplication = content.SourceApplication,
        };

        if (hasFiles)
        {
            var joined = string.Join(Environment.NewLine, content.Files!);
            item.TextContent = joined;
            item.Title = content.Files!.Count == 1 ? Path.GetFileName(content.Files[0]) : $"{content.Files.Count} files — {Path.GetFileName(content.Files[0])}, …";
            item.ContentHash = Hash(Encoding.UTF8.GetBytes("files:" + joined));
            item.SizeBytes = Encoding.UTF8.GetByteCount(joined);
        }
        else if (hasImage)
        {
            item.ContentHash = Hash(content.ImagePng!);
            var fileName = item.ContentHash[..16] + ".png";
            var fullPath = Path.Combine(ImagesFolder, fileName);
            if (!File.Exists(fullPath)) File.WriteAllBytes(fullPath, content.ImagePng!);
            item.BinaryPath = Path.Combine("images", fileName);
            item.SizeBytes = content.ImagePng!.Length;
            item.Title = $"Image {content.ImageWidth} × {content.ImageHeight}";
            item.MetadataJson = JsonSerializer.Serialize(new { width = content.ImageWidth, height = content.ImageHeight, mime = "image/png" });
        }
        else
        {
            var text = content.Text!;
            item.TextContent = text;
            item.ContentHash = Hash(Encoding.UTF8.GetBytes(text));
            item.SizeBytes = Encoding.UTF8.GetByteCount(text);
            item.Title = item.IsSensitive ? ContentClassifier.Mask(text) : ContentClassifier.MakeTitle(text);
            if (item.Kind == ContentKind.Url && ContentClassifier.TryGetHost(text.Trim()) is { } host)
                item.MetadataJson = JsonSerializer.Serialize(new { domain = host });
        }

        item.ExpiresAt = Expiration.ExpiresAt(item, now);

        // Formatting is kept for ordinary text only: never for secrets, and only within the size limit.
        RichText? rich = null;
        if (hasText && !hasFiles && !hasImage && !item.IsSensitive)
        {
            var limit = Settings.MaxTextChars * 4;
            rich = new RichText(
                content.Html is { Length: > 0 } h && h.Length <= limit ? h : null,
                content.Rtf is { Length: > 0 } r && r.Length <= limit ? r : null);
        }

        var (stored, isNew) = _repo.AddOrTouch(item, rich);
        if (isNew) _repo.EnforceMaxItems(Settings.MaxItems).ForEach(DeleteBinary);
        ItemCaptured?.Invoke(this, stored);
        HistoryChanged?.Invoke(this, EventArgs.Empty);
        return (isNew ? CaptureOutcome.Stored : CaptureOutcome.Duplicate, stored);
    }

    public bool IsExcluded(string? sourceApp)
    {
        if (string.IsNullOrWhiteSpace(sourceApp)) return false;
        return Settings.ExcludedApplications.Any(x =>
            !string.IsNullOrWhiteSpace(x) && sourceApp.Contains(x.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public List<ClipboardItem> Search(string? input, int limit = 200) => _repo.Search(SearchQuery.Parse(input), limit);

    public ClipboardItem? Get(long id) => _repo.Get(id);

    public RichText? GetRichText(ClipboardItem item) => item.HasRichText ? _repo.GetRichText(item.Id) : null;

    /// <summary>
    /// What to put on the OS clipboard for <paramref name="item"/>.
    /// <paramref name="plainText"/> drops formatting: text only, file lists become their paths.
    /// Returns null when there is nothing to paste (e.g. plain text of an image, or a missing image file).
    /// </summary>
    public ClipboardPayload? LoadPayload(ClipboardItem item, bool plainText = false)
    {
        if (plainText)
        {
            var text = item.Kind == ContentKind.Image ? null : item.TextContent;
            return string.IsNullOrEmpty(text) ? null : new ClipboardPayload { Text = text, IsSensitive = item.IsSensitive };
        }

        switch (item.Kind)
        {
            case ContentKind.Image:
                var path = FullBinaryPath(item);
                if (path is null || !File.Exists(path)) return null;
                return new ClipboardPayload { ImagePng = File.ReadAllBytes(path), IsSensitive = item.IsSensitive };
            case ContentKind.Files when item.TextContent is not null:
                var files = item.TextContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                return new ClipboardPayload { Files = files, IsSensitive = item.IsSensitive };
            default:
                var rich = GetRichText(item);
                return new ClipboardPayload { Text = item.TextContent ?? "", Html = rich?.Html, Rtf = rich?.Rtf, IsSensitive = item.IsSensitive };
        }
    }

    public void TogglePin(ClipboardItem item)
    {
        item.IsPinned = !item.IsPinned;
        _repo.SetPinned(item.Id, item.IsPinned);
        if (!item.IsPinned)
        {
            item.ExpiresAt = Expiration.ExpiresAt(item, _clock.Now);
            _repo.SetExpiry(item.Id, item.ExpiresAt);
        }
        else item.ExpiresAt = null;
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    public void MarkUsed(ClipboardItem item) => _repo.MarkAccessed(item.Id, _clock.Now);

    public void Delete(ClipboardItem item)
    {
        DeleteBinary(_repo.Delete(item.Id));
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Removes expired items and their files. Returns how many were removed.</summary>
    public int CleanupExpired()
    {
        var paths = _repo.DeleteExpired(_clock.Now);
        paths.ForEach(DeleteBinary);
        // Count includes items without binaries, so re-query is unnecessary; report what changed via event.
        HistoryChanged?.Invoke(this, EventArgs.Empty);
        return paths.Count;
    }

    public void ClearHistory()
    {
        _repo.ClearUnpinned().ForEach(DeleteBinary);
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    public int Count() => _repo.Count();

    public string? FullBinaryPath(ClipboardItem item) =>
        item.BinaryPath is null ? null : Path.Combine(_dataFolder, item.BinaryPath);

    private void DeleteBinary(string? relativePath)
    {
        if (string.IsNullOrEmpty(relativePath)) return;
        var full = Path.GetFullPath(Path.Combine(_dataFolder, relativePath));
        // Only ever delete inside our own data folder.
        if (!full.StartsWith(Path.GetFullPath(_dataFolder), StringComparison.OrdinalIgnoreCase)) return;
        try { if (File.Exists(full)) File.Delete(full); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    public static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    public void Dispose() => _repo.Dispose();
}
