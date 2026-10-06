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

    /// <param name="protector">OS user-bound encryption (DPAPI on Windows). Required when <see cref="AppSettings.EncryptDatabase"/> is on.</param>
    public ClipboardService(string dataFolder, AppSettings settings, IClock? clock = null, bool enableFullText = true, IDataProtector? protector = null)
    {
        _dataFolder = dataFolder;
        Directory.CreateDirectory(dataFolder);
        Directory.CreateDirectory(ImagesFolder);
        Settings = settings;
        Expiration = new ExpirationPolicy(settings);
        _clock = clock ?? new SystemClock();
        _repo = new ClipboardRepository(Path.Combine(dataFolder, "clipboard.db"), enableFullText, protector);
        if (settings.EncryptDatabase != _repo.IsEncrypted && (protector is not null || !settings.EncryptDatabase))
            SetEncryption(settings.EncryptDatabase);
    }

    public bool IsEncrypted => _repo.IsEncrypted;

    private const string EncryptedImageSuffix = ".dpapi";

    /// <summary>
    /// Encrypts (or decrypts) the whole history in place: database fields, the search index (moved to memory) and image files.
    /// Can take a few seconds on a large history.
    /// </summary>
    public void SetEncryption(bool on)
    {
        if (on == _repo.IsEncrypted) return;
        var written = new List<string>();
        var replaced = new List<string>();
        try
        {
            _repo.SetEncrypted(on, item =>
            {
                if (item.Kind != ContentKind.Image || item.BinaryPath is null) return (HashOf(item, null), item.BinaryPath);
                var png = ReadBinary(item); // old format
                if (png is null) return (HashOf(item, null) + ":missing", item.BinaryPath);
                var hash = HashOf(item, png);  // new scheme: the repository already switched modes
                var path = StoreImage(hash, png);
                written.Add(path);
                if (!path.Equals(item.BinaryPath, StringComparison.OrdinalIgnoreCase)) replaced.Add(item.BinaryPath);
                return (hash, path);
            });
        }
        catch
        {
            written.ForEach(DeleteBinary);
            throw;
        }
        replaced.ForEach(DeleteBinary);
        Settings.EncryptDatabase = on;
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Content hash for de-duplication (keyed HMAC when the database is encrypted).</summary>
    public string HashText(string text) => _repo.Hash(Encoding.UTF8.GetBytes(text));

    private string HashOf(ClipboardItem item, byte[]? imagePng) => item.Kind switch
    {
        ContentKind.Image when imagePng is not null => _repo.Hash(imagePng),
        ContentKind.Files => _repo.Hash(Encoding.UTF8.GetBytes("files:" + item.TextContent)),
        // Own namespace, so copying the same text never merges into the snippet.
        ContentKind.Snippet => _repo.Hash(Encoding.UTF8.GetBytes("snippet:" + item.Title + "\0" + item.TextContent)),
        _ => _repo.Hash(Encoding.UTF8.GetBytes(item.TextContent ?? "")),
    };

    /// <returns>The relative path of the stored file (encrypted when the database is).</returns>
    private string StoreImage(string hash, byte[] png)
    {
        var fileName = hash[..16] + ".png" + (_repo.IsEncrypted ? EncryptedImageSuffix : "");
        var fullPath = Path.Combine(ImagesFolder, fileName);
        if (!File.Exists(fullPath))
        {
            var tmp = fullPath + ".tmp";
            File.WriteAllBytes(tmp, _repo.IsEncrypted ? _repo.ProtectBytes(png) : png);
            File.Move(tmp, fullPath, overwrite: true);
        }
        return Path.Combine("images", fileName);
    }

    /// <summary>The item's binary payload (an image), decrypted. Null when the file is missing.</summary>
    public byte[]? ReadBinary(ClipboardItem item)
    {
        var path = FullBinaryPath(item);
        if (path is null || !File.Exists(path)) return null;
        var bytes = File.ReadAllBytes(path);
        return path.EndsWith(EncryptedImageSuffix, StringComparison.OrdinalIgnoreCase) ? _repo.UnprotectBytes(bytes) : bytes;
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
            Workspace = "Default",
            SourceApplication = content.SourceApplication,
        };

        if (hasFiles)
        {
            var joined = string.Join(Environment.NewLine, content.Files!);
            item.TextContent = joined;
            item.Title = content.Files!.Count == 1 ? Path.GetFileName(content.Files[0]) : $"{content.Files.Count} files — {Path.GetFileName(content.Files[0])}, …";
            item.ContentHash = HashOf(item, null);
            item.SizeBytes = Encoding.UTF8.GetByteCount(joined);
        }
        else if (hasImage)
        {
            item.ContentHash = HashOf(item, content.ImagePng);
            item.BinaryPath = StoreImage(item.ContentHash, content.ImagePng!);
            item.SizeBytes = content.ImagePng!.Length;
            item.Title = $"Image {content.ImageWidth} × {content.ImageHeight}";
            item.MetadataJson = JsonSerializer.Serialize(new { width = content.ImageWidth, height = content.ImageHeight, mime = "image/png" });
        }
        else
        {
            var text = content.Text!;
            item.TextContent = text;
            item.ContentHash = HashOf(item, null);
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
    public List<ClipboardItem> SearchByQuery(SearchQuery query, int limit = 200) => _repo.Search(query, limit);

    public void AddRecentSearch(string query)
    {
        Settings.AddRecentSearch(query);
        try { Settings.Save(Path.Combine(DataFolder, "settings.json")); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public void RemoveRecentSearch(string query)
    {
        Settings.RecentSearches.RemoveAll(s => s.Equals(query, StringComparison.OrdinalIgnoreCase));
        try { Settings.Save(Path.Combine(DataFolder, "settings.json")); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public ClipboardItem? Get(long id) => _repo.Get(id);

    public RichText? GetRichText(ClipboardItem item) => item.HasRichText ? _repo.GetRichText(item.Id) : null;

    /// <summary>
    /// What to put on the OS clipboard for <paramref name="item"/>.
    /// <paramref name="plainText"/> drops formatting: text only, file lists become their paths.
    /// Returns null when there is nothing to paste (e.g. plain text of an image, or a missing image file).
    /// </summary>
    /// <param name="template">Values for snippet variables; by default {clipboard} is the newest history text.</param>
    public ClipboardPayload? LoadPayload(ClipboardItem item, bool plainText = false, TemplateContext? template = null)
    {
        if (item.Kind == ContentKind.Snippet)
        {
            template ??= new TemplateContext { Now = _clock.Now.ToLocalTime().DateTime, Clipboard = LatestHistoryText };
            return new ClipboardPayload { Text = TemplateEngine.Expand(item.TextContent ?? "", template) };
        }
        if (plainText)
        {
            string? text;
            if (item.Kind == ContentKind.Image)
            {
                var (qr, ocr) = ParseQrAndOcrText(item.OcrText);
                text = qr ?? ocr;
            }
            else
            {
                text = item.TextContent;
            }
            return string.IsNullOrEmpty(text) ? null : new ClipboardPayload { Text = text, IsSensitive = item.IsSensitive };
        }

        switch (item.Kind)
        {
            case ContentKind.Image:
                var png = ReadBinary(item);
                return png is null ? null : new ClipboardPayload { ImagePng = png, IsSensitive = item.IsSensitive };
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

    public void MarkUsed(ClipboardItem item)
    {
        if (item.Kind == ContentKind.Snippet) _repo.Touch(item.Id, _clock.Now); // recently used snippets come first
        else _repo.MarkAccessed(item.Id, _clock.Now);
    }

    // ---- OCR ----

    /// <summary>Images still waiting for text recognition (e.g. copied before OCR was enabled).</summary>
    public List<ClipboardItem> ImagesWithoutOcr(int limit = 50) => _repo.ImagesWithoutOcr(limit);

    /// <summary>
    /// Stores the text recognized in an image so search finds it and Ctrl+Shift+Enter pastes it.
    /// Text that looks like a secret is not kept (the image is marked as processed with no text).
    /// </summary>
    /// <returns>true when text was stored.</returns>
    public bool AttachOcrText(ClipboardItem item, string? text)
    {
        if (item.Kind != ContentKind.Image) return false;
        var clean = (text ?? "").Replace("\r\n", "\n").Trim();
        if (clean.Length > Settings.MaxTextChars) clean = clean[..(int)Settings.MaxTextChars];
        bool secret = clean.Length > 0 && (SensitiveDataDetector.Detect(clean) is not null ||
                                           clean.Split('\n').Any(l => SensitiveDataDetector.Detect(l) is not null));
        if (secret) clean = "";
        _repo.SetOcrText(item, clean.Replace("\n", Environment.NewLine));
        HistoryChanged?.Invoke(this, EventArgs.Empty);
        return clean.Length > 0;
    }

    /// <summary>Recognizes text in <paramref name="items"/> one by one (failures count as "no text" so they aren't retried forever).</summary>
    public Task<int> RunOcrAsync(IOcrEngine engine, IEnumerable<ClipboardItem> items, CancellationToken cancellationToken = default) =>
        RunOcrAsync(engine, items, null, cancellationToken);

    /// <summary>Recognizes text and/or scans barcodes/QR codes in <paramref name="items"/> one by one.</summary>
    public async Task<int> RunOcrAsync(
        IOcrEngine? engine,
        IEnumerable<ClipboardItem> items,
        IBarcodeScanner? barcodeScanner,
        CancellationToken cancellationToken = default)
    {
        int found = 0;
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item.Kind != ContentKind.Image) continue;
            string text = "";
            try
            {
                var png = ReadBinary(item);
                if (png is { Length: > 0 })
                {
                    var barcodeTexts = barcodeScanner is { IsAvailable: true }
                        ? await barcodeScanner.ScanAsync(png, cancellationToken).ConfigureAwait(false)
                        : Array.Empty<string>();

                    var ocrText = engine is { IsAvailable: true }
                        ? await engine.RecognizeAsync(png, cancellationToken).ConfigureAwait(false)
                        : "";

                    text = CombineImageTexts(barcodeTexts, ocrText);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException or System.Runtime.InteropServices.COMException)
            {
                text = "";
            }
            if (AttachOcrText(item, text)) found++;
        }
        return found;
    }

    private static string CombineImageTexts(IReadOnlyList<string>? barcodes, string? ocrText)
    {
        var cleanBarcodes = barcodes?.Where(b => !string.IsNullOrWhiteSpace(b)).Select(b => b.Trim()).Distinct().ToList()
            ?? new List<string>();
        var cleanOcr = (ocrText ?? "").Trim();

        if (cleanBarcodes.Count == 0) return cleanOcr;

        var barcodeBlock = string.Join(Environment.NewLine, cleanBarcodes);
        if (string.IsNullOrEmpty(cleanOcr)) return barcodeBlock;

        if (cleanOcr.Contains(barcodeBlock, StringComparison.OrdinalIgnoreCase))
            return cleanOcr;

        return $"{barcodeBlock}{Environment.NewLine}{Environment.NewLine}{cleanOcr}";
    }

    /// <summary>
    /// Separates any decoded barcode / QR code block from surrounding OCR text in an image's text.
    /// Returns (QrCode, OtherText).
    /// </summary>
    public static (string? QrCode, string? OtherText) ParseQrAndOcrText(string? rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText)) return (null, null);

        var trimmed = rawText.Trim();
        int splitIdx = trimmed.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        int sepLen = 4;
        if (splitIdx < 0)
        {
            splitIdx = trimmed.IndexOf("\n\n", StringComparison.Ordinal);
            sepLen = 2;
        }

        string firstPart = splitIdx >= 0 ? trimmed[..splitIdx].Trim() : trimmed;
        string? secondPart = splitIdx >= 0 ? trimmed[(splitIdx + sepLen)..].Trim() : null;

        if (IsBarcodeOrQrString(firstPart))
        {
            return (firstPart, string.IsNullOrWhiteSpace(secondPart) ? null : secondPart);
        }

        if (IsBarcodeOrQrString(trimmed))
        {
            return (trimmed, null);
        }

        return (null, trimmed);
    }

    public static bool IsBarcodeOrQrString(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        var trimmed = s.Trim();
        return trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("WIFI:", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("BEGIN:VCARD", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("tel:", StringComparison.OrdinalIgnoreCase)
            || (long.TryParse(trimmed, out _) && trimmed.Length >= 8);
    }

    // ---- Snippets ----

    /// <summary>Text of the most recent copy that is neither a snippet, an image nor a secret.</summary>
    public string? LatestHistoryText() =>
        _repo.Search(new SearchQuery { Sensitive = false }, 20, pinnedFirst: false)
            .FirstOrDefault(i => i.Kind is not (ContentKind.Snippet or ContentKind.Image) && !string.IsNullOrEmpty(i.TextContent))?.TextContent;

    public List<ClipboardItem> Snippets() => _repo.Search(new SearchQuery { Kind = ContentKind.Snippet }, int.MaxValue, pinnedFirst: false)
        .OrderBy(s => s.Title, StringComparer.CurrentCultureIgnoreCase).ToList();

    /// <summary>Creates a snippet, or updates <paramref name="existing"/>. Snippets never expire.</summary>
    /// <exception cref="ArgumentException">Empty name/body, or another snippet already has the same name and text.</exception>
    public ClipboardItem SaveSnippet(string name, string body, ClipboardItem? existing = null)
    {
        name = name.Trim();
        if (name.Length == 0) throw new ArgumentException("A snippet needs a name.", nameof(name));
        if (string.IsNullOrEmpty(body)) throw new ArgumentException("A snippet needs some text.", nameof(body));

        var now = _clock.Now;
        var item = existing ?? new ClipboardItem { Kind = ContentKind.Snippet, CreatedAt = now, LastCopiedAt = now, Confidence = 1 };
        if (item.Kind != ContentKind.Snippet) throw new ArgumentException("Not a snippet.", nameof(existing));
        item.Subtype = TemplateEngine.HasVariables(body) ? "template" : "snippet";
        item.Title = name;
        item.TextContent = body;
        if (existing is null) item.Workspace = "Default";
        item.SizeBytes = Encoding.UTF8.GetByteCount(body);
        item.ContentHash = HashOf(item, null);
        item.ExpiresAt = null;

        if (existing is null)
        {
            var (stored, isNew) = _repo.AddOrTouch(item);
            if (!isNew) throw new ArgumentException($"A snippet named \"{name}\" with this text already exists.");
            item = stored;
        }
        else
        {
            if (_repo.ExistsWithHash(item.ContentHash, item.Workspace, item.Id))
                throw new ArgumentException($"A snippet named \"{name}\" with this text already exists.");
            _repo.UpdateContent(item);
        }
        HistoryChanged?.Invoke(this, EventArgs.Empty);
        return item;
    }

    /// <summary>A history item as the starting point of a new snippet (name = its title).</summary>
    public static (string Name, string Body) SnippetDraftFrom(ClipboardItem item) =>
        (item.Kind == ContentKind.Snippet ? item.Title : ContentClassifier.MakeTitle(item.TextContent, 40), item.TextContent ?? "");

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
