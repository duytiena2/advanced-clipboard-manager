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

    // ---- #5 Text transforms ----

    [Test]
    public void Case_and_whitespace_transforms()
    {
        Assert.Equal("HELLO WORLD", TextTransforms.Apply("upper", "Hello World"));
        Assert.Equal("hello world", TextTransforms.Apply("lower", "Hello World"));
        Assert.Equal("Hello World", TextTransforms.Apply("title", "hELLO wORLD"));
        Assert.Equal("Hello there. How are you?", TextTransforms.Apply("sentence", "HELLO THERE. HOW ARE YOU?"));
        var nl = Environment.NewLine;
        Assert.Equal("a b" + nl + "c", TextTransforms.Apply("trim", "  a \t  b  \n   c   \n\n"));
        Assert.Equal("a b c", TextTransforms.Apply("one-line", "a\n  b\r\nc"));
        Assert.Equal("a" + nl + "b", TextTransforms.Apply("remove-blank-lines", "a\n\n   \nb"));
    }

    [Test]
    public void Json_transforms()
    {
        var nl = Environment.NewLine;
        Assert.Equal("{" + nl + "  \"a\": 1," + nl + "  \"b\": \"chào\"" + nl + "}", TextTransforms.Apply("json-pretty", "{\"a\":1,\"b\":\"chào\"}").Replace("\n", nl).Replace("\r" + nl, nl));
        Assert.Equal("{\"a\":[1,2]}", TextTransforms.Apply("json-minify", "{ \"a\": [ 1, 2 ] }"));
        Throws<TransformException>(() => TextTransforms.Apply("json-pretty", "{ not json"));
    }

    [Test]
    public void Base64_and_url_transforms()
    {
        Assert.Equal("WGluIGNow6Bv", TextTransforms.Apply("base64-encode", "Xin chào"));
        Assert.Equal("Xin chào", TextTransforms.Apply("base64-decode", "WGluIGNow6Bv"));
        Assert.Equal("hi?", TextTransforms.Apply("base64-decode", "aGk_")); // base64url, no padding
        Throws<TransformException>(() => TextTransforms.Apply("base64-decode", "%%%"));
        Throws<TransformException>(() => TextTransforms.Apply("base64-decode", "/w==")); // 0xFF is not UTF-8 text
        Assert.Equal("a%20b%26c%3D%C4%91", TextTransforms.Apply("url-encode", "a b&c=đ"));
        Assert.Equal("a b&c=đ", TextTransforms.Apply("url-decode", "a%20b%26c%3D%C4%91"));
    }

    [Test]
    public void Sql_formatter_puts_clauses_on_lines()
    {
        var nl = Environment.NewLine;
        var sql = "select u.id, u.name, count(o.id) as orders from users u left join orders o on o.user_id = u.id " +
                  "where u.active = 1 and u.created_at between '2026-01-01' and '2026-12-31' group by u.id, u.name order by orders desc limit 10";
        var expected = string.Join(nl,
            "SELECT u.id,",
            "    u.name,",
            "    COUNT(o.id) AS orders",
            "FROM users u",
            "LEFT JOIN orders o",
            "    ON o.user_id = u.id",
            "WHERE u.active = 1",
            "    AND u.created_at BETWEEN '2026-01-01' AND '2026-12-31'",
            "GROUP BY u.id, u.name",
            "ORDER BY orders DESC",
            "LIMIT 10");
        Assert.Equal(expected, TextTransforms.Apply("sql", sql));
    }

    [Test]
    public void Sql_formatter_keeps_strings_and_handles_subqueries()
    {
        var nl = Environment.NewLine;
        var formatted = SqlFormatter.Format("SELECT * FROM t WHERE name = 'select from where' AND id IN (SELECT id FROM x);");
        var expected = string.Join(nl,
            "SELECT *",
            "FROM t",
            "WHERE name = 'select from where'",
            "    AND id IN (",
            "        SELECT id",
            "        FROM x",
            "    );");
        Assert.Equal(expected, formatted);
        Assert.Equal("UPDATE t" + nl + "SET a = 1," + nl + "    b = 'x'" + nl + "WHERE id = 2", SqlFormatter.Format("update t set a = 1, b = 'x' where id = 2"));
    }

    // ---- #10 Workspace by app ----

    [Test]
    public void Workspace_rules_route_copies_by_source_app()
    {
        var settings = new AppSettings
        {
            DefaultWorkspace = "Inbox",
            WorkspaceRules = new() { new("Code", "Dev"), new("OUTLOOK.EXE", "Mail"), new("", "Ignored"), new("devenv", " ") },
        };
        var svc = NewService(settings);
        Assert.Equal("Dev", svc.Capture(CapturedContent.FromText("git status", "code")).Item!.Workspace);
        Assert.Equal("Mail", svc.Capture(CapturedContent.FromText("Dear team", "OUTLOOK")).Item!.Workspace);
        Assert.Equal("Inbox", svc.Capture(CapturedContent.FromText("from notepad", "notepad")).Item!.Workspace);
        Assert.Equal("Inbox", svc.Capture(CapturedContent.FromText("blank rule", "devenv")).Item!.Workspace);
        Assert.Equal("Inbox", svc.Capture(CapturedContent.FromText("unknown source")).Item!.Workspace);
        Assert.Equal("Inbox", svc.WorkspaceFor("CodeHelper"), "exact process name, not a substring");

        Assert.Equal(1, svc.Search("workspace:dev").Count);
        var names = svc.Workspaces().Select(w => w.Name).ToList();
        Assert.True(names.SequenceEqual(new[] { "Dev", "Inbox", "Mail" }), string.Join(",", names));
    }

    [Test]
    public void Workspace_rules_roundtrip_in_settings()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "ws.json");
        new AppSettings { WorkspaceRules = new() { new("Code", "Dev") } }.Save(path);
        var loaded = AppSettings.Load(path);
        Assert.Equal(1, loaded.WorkspaceRules.Count);
        Assert.Equal("Dev", loaded.WorkspaceRules[0].Workspace);
    }

    private static void Throws<TEx>(Action a) where TEx : Exception
    {
        try { a(); }
        catch (TEx) { return; }
        throw new AssertionException("Expected " + typeof(TEx).Name);
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
