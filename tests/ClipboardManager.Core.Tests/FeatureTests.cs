using System.Security.Cryptography;
using ClipboardManager.Core.Models;
using ClipboardManager.Core.Platform;
using ClipboardManager.Core.Services;

namespace ClipboardManager.Core.Tests;

/// <summary>Stand-in for DPAPI: AES with a per-"user" key; a wrong key fails like DPAPI does for another account.</summary>
internal sealed class FakeProtector : IDataProtector
{
    private readonly byte[] _key;
    public FakeProtector(byte key = 7) => _key = Enumerable.Repeat(key, 32).ToArray();

    public byte[] Protect(byte[] plain)
    {
        using var aes = Aes.Create();
        aes.Key = _key;
        var iv = RandomNumberGenerator.GetBytes(16);
        var cipher = aes.EncryptCbc(plain, iv);
        var mac = HMACSHA256.HashData(_key, cipher);
        return iv.Concat(cipher).Concat(mac).ToArray();
    }

    public byte[] Unprotect(byte[] data)
    {
        if (data.Length < 48) throw new CryptographicException("too short");
        var cipher = data[16..^32];
        if (!HMACSHA256.HashData(_key, cipher).AsSpan().SequenceEqual(data.AsSpan(data.Length - 32))) throw new CryptographicException("bad key");
        using var aes = Aes.Create();
        aes.Key = _key;
        return aes.DecryptCbc(cipher, data[..16]);
    }
}

/// <summary>Phase 2/3 features: rich text, transforms, workspaces by app, encryption, snippets, paste stack, OCR.</summary>
public sealed class FeatureTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "acm-feat-" + Guid.NewGuid().ToString("N"));
    private readonly List<ClipboardService> _services = new();

    private ClipboardService NewService(AppSettings? settings = null, FakeClock? clock = null, bool fts = true, string? folder = null,
        IDataProtector? protector = null)
    {
        var svc = new ClipboardService(folder ?? Path.Combine(_dir, _services.Count.ToString()), settings ?? new AppSettings(), clock ?? new FakeClock(), fts, protector);
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

    // ---- #7 Database encryption ----

    private static byte[] ReadDbFiles(string folder) =>
        Directory.GetFiles(folder, "clipboard.db*").SelectMany(f =>
        {
            using var s = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        }).ToArray();

    private static bool Contains(byte[] haystack, string needle) =>
        haystack.AsSpan().IndexOf(System.Text.Encoding.UTF8.GetBytes(needle)) >= 0;

    [Test]
    public void Encrypted_database_has_no_plaintext_on_disk_and_still_searches()
    {
        var folder = Path.Combine(_dir, "enc");
        var settings = new AppSettings { EncryptDatabase = true };
        var protector = new FakeProtector();
        var svc = NewService(settings, folder: folder, protector: protector);
        Assert.True(svc.IsEncrypted);
        svc.Capture(new CapturedContent { Text = "zebracorn secret plan", Html = "<b>zebracorn html</b>" });
        var (_, img) = svc.Capture(CapturedContent.FromImage(new byte[] { 0x89, 0x50, 0x4E, 0x47, 42, 42, 42 }, 2, 2));

        Assert.Equal(1, svc.Search("zebracorn").Count);
        Assert.Equal("zebracorn secret plan", svc.Search("zebra")[0].TextContent);
        Assert.Equal("<b>zebracorn html</b>", svc.GetRichText(svc.Search("zebra")[0])!.Html);
        Assert.Equal(7, svc.ReadBinary(img!)!.Length);
        Assert.True(img!.BinaryPath!.EndsWith(".dpapi"));
        var imageFile = File.ReadAllBytes(svc.FullBinaryPath(img)!);
        Assert.False(imageFile.AsSpan().IndexOf(new byte[] { 42, 42, 42 }) >= 0, "image file is encrypted");

        svc.Dispose();
        _services.Remove(svc);
        Assert.False(Contains(ReadDbFiles(folder), "zebracorn"), "no plaintext in clipboard.db / -wal");

        // Reopen: the in-memory index is rebuilt, duplicates are still detected through the keyed hash.
        var again = NewService(settings, folder: folder, protector: protector);
        Assert.Equal(1, again.Search("zebracorn").Count);
        Assert.Equal(CaptureOutcome.Duplicate, again.Capture(CapturedContent.FromText("zebracorn secret plan")).Outcome);
        Assert.Equal(2, again.Count());
    }

    [Test]
    public void Encryption_can_be_turned_on_and_off_in_place()
    {
        var folder = Path.Combine(_dir, "toggle");
        var protector = new FakeProtector();
        var settings = new AppSettings();
        var svc = NewService(settings, folder: folder, protector: protector);
        var (_, plain) = svc.Capture(CapturedContent.FromText("quokka notes"));
        var (_, img) = svc.Capture(CapturedContent.FromImage(new byte[] { 1, 2, 3, 4 }, 1, 1));
        var plainHash = plain!.ContentHash;
        Assert.Equal(ClipboardService.Hash(System.Text.Encoding.UTF8.GetBytes("quokka notes")), plainHash);

        svc.SetEncryption(true);
        Assert.True(settings.EncryptDatabase);
        Assert.Equal(1, svc.Search("quokka").Count);
        Assert.True(svc.Search("quokka")[0].ContentHash != plainHash, "hash is keyed once encrypted");
        Assert.Equal(1, Directory.GetFiles(svc.ImagesFolder).Length);
        var encImage = svc.Search("type:image")[0];
        Assert.Equal(4, svc.ReadBinary(encImage)!.Length);
        Assert.False(Contains(ReadDbFiles(folder), "quokka"), "plaintext gone after VACUUM");

        svc.SetEncryption(false);
        Assert.False(svc.IsEncrypted);
        Assert.Equal(plainHash, svc.Search("quokka")[0].ContentHash);
        var back = svc.Search("type:image")[0];
        Assert.False(back.BinaryPath!.EndsWith(".dpapi"));
        Assert.Equal(1, Directory.GetFiles(svc.ImagesFolder).Length);
        Assert.Equal(CaptureOutcome.Duplicate, svc.Capture(CapturedContent.FromImage(new byte[] { 1, 2, 3, 4 }, 1, 1)).Outcome);
    }

    [Test]
    public void Encrypted_database_needs_the_right_protector()
    {
        var folder = Path.Combine(_dir, "other-user");
        var svc = NewService(new AppSettings { EncryptDatabase = true }, folder: folder, protector: new FakeProtector(key: 1));
        svc.Capture(CapturedContent.FromText("private"));
        svc.Dispose();
        _services.Remove(svc);

        Throws<InvalidOperationException>(() => new ClipboardService(folder, new AppSettings { EncryptDatabase = true }, new FakeClock(), protector: null));
        Throws<InvalidOperationException>(() => new ClipboardService(folder, new AppSettings { EncryptDatabase = true }, new FakeClock(), protector: new FakeProtector(key: 2)));
    }

    [Test]
    public void Encrypted_search_without_fts5_filters_after_decrypting()
    {
        var svc = NewService(new AppSettings { EncryptDatabase = true }, fts: false, protector: new FakeProtector());
        svc.Capture(CapturedContent.FromText("Xin chào thế giới"));
        svc.Capture(CapturedContent.FromText("docker compose up"));
        svc.Capture(CapturedContent.FromText("sk_live_51HxAbCdEfGhIjKlMnOpQrStUv"));
        Assert.Equal(1, svc.Search("chao").Count);
        Assert.Equal(1, svc.Search("COMPOSE docker").Count);
        Assert.Equal(0, svc.Search("AbCdEf").Count, "secrets not searchable by content");
    }

    // ---- #9 Snippets & templates ----

    [Test]
    public void Template_variables_expand()
    {
        var ctx = new TemplateContext
        {
            Now = new DateTime(2026, 10, 3, 14, 5, 9),
            Culture = System.Globalization.CultureInfo.GetCultureInfo("vi-VN"),
            Clipboard = () => "ORDER-42",
        };
        Assert.Equal("Ngày 03/10/2026 lúc 14:05", TemplateEngine.Expand("Ngày {date} lúc {time}", ctx));
        Assert.Equal("2026-10-03 14:05:09", TemplateEngine.Expand("{date:yyyy-MM-dd} {time:HH:mm:ss}", ctx));
        Assert.Equal("Re: ORDER-42 / ORDER-42", TemplateEngine.Expand("Re: {clipboard} / {CLIPBOARD}", ctx));
        Assert.Equal("{name} {} {{x}} {date:", TemplateEngine.Expand("{name} {} {{{{x}}}} {date:", ctx));
        Assert.Equal("{literal}", TemplateEngine.Expand("{{literal}}", ctx));
        Assert.Equal(36, TemplateEngine.Expand("{uuid}", ctx).Length);
    }

    [Test]
    public void Snippets_are_saved_searched_and_expanded()
    {
        var clock = new FakeClock();
        var svc = NewService(clock: clock);
        svc.Capture(CapturedContent.FromText("ticket ABC-1"));
        var sig = svc.SaveSnippet("Email signature", "Best regards,\nTiến");
        var tpl = svc.SaveSnippet("Reply", "Hi, about {clipboard}: done on {date:yyyy-MM-dd}.");
        Assert.Equal("template", tpl.Subtype);
        Assert.Equal("Snippet", sig.DisplayType);

        Assert.Equal(2, svc.Search("type:snippet").Count);
        Assert.Equal(1, svc.Search("signature").Count);
        Assert.Equal("Hi, about ticket ABC-1: done on 2026-10-02.", svc.LoadPayload(tpl)!.Text);
        Assert.True(svc.Snippets().Select(s => s.Title).SequenceEqual(new[] { "Email signature", "Reply" }));

        // Copying the snippet's text is a separate history item, not a bump of the snippet.
        Assert.Equal(CaptureOutcome.Stored, svc.Capture(CapturedContent.FromText("Best regards,\nTiến")).Outcome);

        var edited = svc.SaveSnippet("Email signature", "Thanks,\nTiến", existing: sig);
        Assert.Equal(sig.Id, edited.Id);
        Assert.Equal("Thanks,\nTiến", svc.Get(sig.Id)!.TextContent);
        Assert.Equal(1, svc.Search("thanks").Count);
        Assert.Equal(0, svc.Search("regards type:snippet").Count, "old text removed from the index");
        Throws<ArgumentException>(() => svc.SaveSnippet("Reply", "Hi, about {clipboard}: done on {date:yyyy-MM-dd}."));
        Throws<ArgumentException>(() => svc.SaveSnippet("  ", "x"));
    }

    [Test]
    public void Snippets_survive_clear_expiry_and_max_items()
    {
        var clock = new FakeClock();
        var svc = NewService(new AppSettings { MaxItems = 2 }, clock);
        var snip = svc.SaveSnippet("Addr", "123 Lê Lợi, Q1");
        Assert.Null(snip.ExpiresAt);
        for (int i = 0; i < 5; i++) { clock.Advance(TimeSpan.FromSeconds(1)); svc.Capture(CapturedContent.FromText("item " + i)); }
        Assert.Equal(3, svc.Count(), "2 history items + the snippet");
        clock.Advance(TimeSpan.FromDays(30));
        svc.CleanupExpired();
        svc.ClearHistory();
        Assert.Equal(1, svc.Count());
        Assert.Equal("Addr", svc.Search(null)[0].Title);
    }

    [Test]
    public void Using_a_snippet_moves_it_up()
    {
        var clock = new FakeClock();
        var svc = NewService(clock: clock);
        var snip = svc.SaveSnippet("Old", "old snippet");
        clock.Advance(TimeSpan.FromMinutes(1));
        svc.Capture(CapturedContent.FromText("newer copy"));
        Assert.Equal("newer copy", svc.Search(null)[0].TextContent);
        clock.Advance(TimeSpan.FromMinutes(1));
        svc.MarkUsed(snip);
        Assert.Equal("Old", svc.Search(null)[0].Title);
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
