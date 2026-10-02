using ClipboardManager.Core.Models;
using ClipboardManager.Core.Services;

namespace ClipboardManager.Core.Tests;

/// <summary>Phase 2/3 features: rich text, transforms, workspaces by app, encryption, snippets, paste stack, OCR.</summary>
public sealed class FeatureTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "acm-feat-" + Guid.NewGuid().ToString("N"));
    private readonly List<ClipboardService> _services = new();

    private ClipboardService NewService(AppSettings? settings = null, FakeClock? clock = null, bool fts = true, string? folder = null)
    {
        var svc = new ClipboardService(folder ?? Path.Combine(_dir, _services.Count.ToString()), settings ?? new AppSettings(), clock ?? new FakeClock(), fts);
        _services.Add(svc);
        return svc;
    }

    public void Dispose()
    {
        foreach (var s in _services) s.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    // ---- #1 Plain-text paste / rich formats ----

    private const string Html = "Version:0.9\r\nStartHTML:0000000105\r\n<html><body><b>Xin chào</b></body></html>";

    [Test]
    public void Rich_text_is_stored_and_restored()
    {
        var svc = NewService();
        var (_, item) = svc.Capture(new CapturedContent { Text = "Xin chào", Html = Html, Rtf = @"{\rtf1 Xin}" });
        Assert.True(item!.HasRichText);
        var listed = svc.Search(null)[0];
        Assert.True(listed.HasRichText, "flag loaded by list queries");

        var rich = svc.LoadPayload(listed)!;
        Assert.Equal("Xin chào", rich.Text);
        Assert.Equal(Html, rich.Html);
        Assert.Equal(@"{\rtf1 Xin}", rich.Rtf);

        var plain = svc.LoadPayload(listed, plainText: true)!;
        Assert.Equal("Xin chào", plain.Text);
        Assert.Null(plain.Html);
        Assert.Null(plain.Rtf);
    }

    [Test]
    public void Recopy_without_formatting_replaces_old_formatting()
    {
        var svc = NewService();
        svc.Capture(new CapturedContent { Text = "same text", Html = Html });
        var (_, item) = svc.Capture(CapturedContent.FromText("same text"));
        Assert.False(item!.HasRichText);
        Assert.Null(svc.LoadPayload(svc.Get(item.Id)!)!.Html);
    }

    [Test]
    public void Secrets_never_keep_formatting()
    {
        var svc = NewService();
        var (_, item) = svc.Capture(new CapturedContent { Text = "sk_live_51HxAbCdEfGhIjKlMnOpQrStUv", Html = Html });
        Assert.True(item!.IsSensitive);
        Assert.False(item.HasRichText);
        Assert.True(svc.LoadPayload(item)!.IsSensitive);
    }

    [Test]
    public void Plain_text_of_files_is_their_paths_and_images_have_none()
    {
        var svc = NewService();
        var (_, files) = svc.Capture(CapturedContent.FromFiles(new[] { @"C:\a.txt", @"C:\b.txt" }));
        Assert.Equal(2, svc.LoadPayload(files!)!.Files!.Count);
        Assert.Equal(@"C:\a.txt" + Environment.NewLine + @"C:\b.txt", svc.LoadPayload(files!, plainText: true)!.Text);

        var (_, img) = svc.Capture(CapturedContent.FromImage(new byte[] { 1, 2, 3 }, 1, 1));
        Assert.Equal(3, svc.LoadPayload(img!)!.ImagePng!.Length);
        Assert.Null(svc.LoadPayload(img!, plainText: true));
    }

    [Test]
    public void Schema_v1_database_is_upgraded()
    {
        var folder = Path.Combine(_dir, "v1");
        Directory.CreateDirectory(folder);
        using (var db = new Storage.Sqlite.SqliteDb(Path.Combine(folder, "clipboard.db")))
        {
            db.Execute(@"CREATE TABLE clipboard_items (id INTEGER PRIMARY KEY AUTOINCREMENT, content_type TEXT NOT NULL, subtype TEXT NOT NULL DEFAULT '',
                title TEXT NOT NULL DEFAULT '', text_content TEXT NULL, binary_path TEXT NULL, content_hash TEXT NOT NULL, size_bytes INTEGER NOT NULL DEFAULT 0,
                created_at INTEGER NOT NULL, last_copied_at INTEGER NOT NULL, accessed_at INTEGER NULL, expires_at INTEGER NULL,
                is_pinned INTEGER NOT NULL DEFAULT 0, is_sensitive INTEGER NOT NULL DEFAULT 0, copy_count INTEGER NOT NULL DEFAULT 1,
                workspace TEXT NOT NULL DEFAULT 'Default', source_application TEXT NULL, detection_confidence REAL NOT NULL DEFAULT 0, metadata_json TEXT NULL);");
            db.Execute("INSERT INTO clipboard_items (content_type, title, text_content, content_hash, created_at, last_copied_at) VALUES ('Text', 'old', 'old item', 'h', 1, 1);");
        }
        var svc = NewService(folder: folder, fts: false);
        var items = svc.Search(null);
        Assert.Equal(1, items.Count);
        Assert.False(items[0].HasRichText);
        svc.Capture(new CapturedContent { Text = "new", Html = Html });
        Assert.Equal(2, svc.Count());
    }
}
