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

/// <summary>Phase 2/3 features: rich text, transforms, encryption, snippets, paste stack, OCR.</summary>
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
        var culture = (System.Globalization.CultureInfo)System.Globalization.CultureInfo.GetCultureInfo("vi-VN").Clone();
        culture.DateTimeFormat.ShortDatePattern = "dd/MM/yyyy";
        culture.DateTimeFormat.ShortTimePattern = "HH:mm";
        var ctx = new TemplateContext
        {
            Now = new DateTime(2026, 10, 3, 14, 5, 9),
            Culture = culture,
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

    // ---- #4 Paste stack ----

    [Test]
    public void Paste_stack_walks_items_in_order()
    {
        var items = new[] { "Nguyễn Văn A", "a@example.com", "0901234567" }
            .Select((t, i) => new ClipboardItem { Id = i + 1, TextContent = t }).ToList();
        var stack = new PasteStack(items);
        Assert.Equal(3, stack.Count);
        Assert.Equal("Nguyễn Văn A", stack.Current!.TextContent);
        Assert.Equal(1, stack.Position);
        Assert.Equal("a@example.com", stack.Advance()!.TextContent);
        Assert.Equal(2, stack.Remaining);
        Assert.Equal("0901234567", stack.Advance()!.TextContent);
        Assert.Null(stack.Advance());
        Assert.True(stack.IsFinished);
        Assert.Null(stack.Advance(), "advancing past the end is harmless");
        Assert.Equal(0, stack.Remaining);
        Throws<ArgumentException>(() => new PasteStack(Array.Empty<ClipboardItem>()));
    }

    [Test]
    public void Search_folds_vietnamese_d()
    {
        foreach (var fts in new[] { true, false })
        {
            var svc = NewService(fts: fts);
            svc.Capture(CapturedContent.FromText("Đơn hàng đã giao"));
            Assert.Equal(1, svc.Search("don hang").Count, "fts=" + fts);
            Assert.Equal(1, svc.Search("da giao").Count, "fts=" + fts);
            Assert.Equal(1, svc.Search("đơn").Count, "fts=" + fts);
        }
    }

    // ---- #8 OCR ----

    private sealed class FakeOcr : IOcrEngine
    {
        public Func<byte[], string> Recognize { get; init; } = _ => "";
        public bool IsAvailable => true;
        public Task<string> RecognizeAsync(byte[] png, CancellationToken cancellationToken = default) => Task.FromResult(Recognize(png));
    }

    [Test]
    public void Ocr_text_makes_images_searchable_and_pasteable()
    {
        foreach (var (fts, encrypt) in new[] { (true, false), (false, false), (true, true), (false, true) })
        {
            var svc = NewService(new AppSettings { EncryptDatabase = encrypt }, fts: fts, protector: new FakeProtector());
            var (_, img) = svc.Capture(CapturedContent.FromImage(new byte[] { 1, 2, 3 }, 10, 10));
            var (_, blank) = svc.Capture(CapturedContent.FromImage(new byte[] { 9, 9 }, 10, 10));
            Assert.Equal(2, svc.ImagesWithoutOcr().Count);

            var ocr = new FakeOcr { Recognize = png => png[0] == 1 ? "Hóa đơn số 1234\r\nTổng cộng 500.000đ" : "" };
            int found = svc.RunOcrAsync(ocr, svc.ImagesWithoutOcr()).GetAwaiter().GetResult();
            Assert.Equal(1, found);
            Assert.Equal(0, svc.ImagesWithoutOcr().Count, "both processed, even the one without text");

            var hits = svc.Search("hoa don");
            Assert.Equal(1, hits.Count, $"fts={fts} encrypt={encrypt}");
            Assert.Equal(img!.Id, hits[0].Id);
            Assert.Equal("Hóa đơn số 1234" + Environment.NewLine + "Tổng cộng 500.000đ", svc.LoadPayload(hits[0], plainText: true)!.Text);
            Assert.Null(svc.LoadPayload(svc.Get(blank!.Id)!, plainText: true), "no text found = nothing to paste as text");
            Assert.True(svc.LoadPayload(hits[0])!.ImagePng is { Length: 3 }, "normal paste is still the image");
        }
    }

    [Test]
    public void Ocr_text_with_a_secret_is_not_kept()
    {
        var svc = NewService();
        var (_, img) = svc.Capture(CapturedContent.FromImage(new byte[] { 5 }, 1, 1));
        Assert.False(svc.AttachOcrText(img!, "config\nsk_live_51HxAbCdEfGhIjKlMnOpQrStUv\nend"));
        Assert.Equal("", svc.Get(img!.Id)!.OcrText);
        Assert.Equal(0, svc.Search("config").Count);
        Assert.Equal(0, svc.ImagesWithoutOcr().Count);
    }

    private sealed class FakeBarcodeScanner : IBarcodeScanner
    {
        public Func<byte[], IReadOnlyList<string>> Scan { get; init; } = _ => Array.Empty<string>();
        public bool IsAvailable => true;
        public Task<IReadOnlyList<string>> ScanAsync(byte[] imageBytes, CancellationToken cancellationToken = default) =>
            Task.FromResult(Scan(imageBytes));
    }

    [Test]
    public void Barcode_scanner_and_ocr_combine_results_in_image()
    {
        var svc = NewService();
        var (_, imgQrOnly) = svc.Capture(CapturedContent.FromImage(new byte[] { 10 }, 10, 10));
        var (_, imgOcrOnly) = svc.Capture(CapturedContent.FromImage(new byte[] { 20 }, 10, 10));
        var (_, imgBoth) = svc.Capture(CapturedContent.FromImage(new byte[] { 30 }, 10, 10));

        var scanner = new FakeBarcodeScanner
        {
            Scan = bytes => bytes[0] switch
            {
                10 => new[] { "https://qrfy.com/scan" },
                30 => new[] { "https://example.com/qr123" },
                _ => Array.Empty<string>()
            }
        };

        var ocr = new FakeOcr
        {
            Recognize = bytes => bytes[0] switch
            {
                20 => "Download your QR\nDownload QR v",
                30 => "Scan this code below",
                _ => ""
            }
        };

        int found = svc.RunOcrAsync(ocr, svc.ImagesWithoutOcr(), scanner).GetAwaiter().GetResult();
        Assert.Equal(3, found);

        // QR only
        var itemQr = svc.Get(imgQrOnly!.Id)!;
        Assert.Equal("https://qrfy.com/scan", itemQr.OcrText);
        Assert.Equal(1, svc.Search("qrfy").Count);

        // OCR only
        var itemOcr = svc.Get(imgOcrOnly!.Id)!;
        Assert.Equal("Download your QR" + Environment.NewLine + "Download QR v", itemOcr.OcrText);
        Assert.Equal(1, svc.Search("Download").Count);

        // Both: QR code first, then OCR text
        var itemBoth = svc.Get(imgBoth!.Id)!;
        var expectedBoth = "https://example.com/qr123" + Environment.NewLine + Environment.NewLine + "Scan this code below";
        Assert.Equal(expectedBoth, itemBoth.OcrText);
        Assert.Equal(1, svc.Search("example.com").Count);
        Assert.Equal(1, svc.Search("code below").Count);
    }

    [Test]
    public void Barcode_with_secret_is_not_kept()
    {
        var svc = NewService();
        var (_, img) = svc.Capture(CapturedContent.FromImage(new byte[] { 40 }, 10, 10));
        var scanner = new FakeBarcodeScanner
        {
            Scan = _ => new[] { "sk_live_51HxAbCdEfGhIjKlMnOpQrStUv" }
        };

        svc.RunOcrAsync(null, new[] { img! }, scanner).GetAwaiter().GetResult();
        Assert.Equal("", svc.Get(img!.Id)!.OcrText);
        Assert.Equal(0, svc.Search("sk_live").Count);
    }

    [Test]
    public void Zxing_qr_code_encode_and_decode()
    {
        var writer = new ZXing.BarcodeWriterPixelData
        {
            Format = ZXing.BarcodeFormat.QR_CODE,
            Options = new ZXing.Common.EncodingOptions
            {
                Width = 100,
                Height = 100,
                Margin = 1
            }
        };
        var pixelData = writer.Write("https://github.com/duytiena2");
        var lum = new ZXing.RGBLuminanceSource(pixelData.Pixels, pixelData.Width, pixelData.Height, ZXing.RGBLuminanceSource.BitmapFormat.RGBA32);
        var reader = new ZXing.BarcodeReaderGeneric();
        var results = reader.DecodeMultiple(lum);
        Assert.NotNull(results);
        Assert.True(results!.Length > 0);
        Assert.Equal("https://github.com/duytiena2", results[0].Text);
    }

    [Test]
    public void Ocr_survives_encryption_toggle()
    {
        var svc = NewService(protector: new FakeProtector());
        var (_, img) = svc.Capture(CapturedContent.FromImage(new byte[] { 7, 7 }, 1, 1));
        svc.AttachOcrText(img!, "wombat invoice");
        svc.SetEncryption(true);
        Assert.Equal(1, svc.Search("wombat").Count);
        Assert.Equal("wombat invoice", svc.Search("wombat")[0].OcrText);
        svc.SetEncryption(false);
        Assert.Equal(1, svc.Search("wombat").Count);
    }

    // ---- #6 Settings UI helpers ----

    [Test]
    public void Retention_minutes_in_words()
    {
        Assert.Equal("never expires", Humanize.Minutes(0));
        Assert.Equal("1 minute", Humanize.Minutes(1));
        Assert.Equal("5 minutes", Humanize.Minutes(5));
        Assert.Equal("1 hour", Humanize.Minutes(60));
        Assert.Equal("1.5 hours", Humanize.Minutes(90));
        Assert.Equal("1 day", Humanize.Minutes(1440));
        Assert.Equal("7 days", Humanize.Minutes(10080));
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

    [Test]
    public void Schema_v6_database_resets_all_images_for_rescanning()
    {
        var folder = Path.Combine(_dir, "v6_upgrade");
        Directory.CreateDirectory(folder);
        using (var db = new Storage.Sqlite.SqliteDb(Path.Combine(folder, "clipboard.db")))
        {
            db.Execute(@"CREATE TABLE clipboard_items (id INTEGER PRIMARY KEY AUTOINCREMENT, content_type TEXT NOT NULL, subtype TEXT NOT NULL DEFAULT '',
                title TEXT NOT NULL DEFAULT '', text_content TEXT NULL, binary_path TEXT NULL, content_hash TEXT NOT NULL, size_bytes INTEGER NOT NULL DEFAULT 0,
                created_at INTEGER NOT NULL, last_copied_at INTEGER NOT NULL, accessed_at INTEGER NULL, expires_at INTEGER NULL,
                is_pinned INTEGER NOT NULL DEFAULT 0, is_sensitive INTEGER NOT NULL DEFAULT 0, copy_count INTEGER NOT NULL DEFAULT 1,
                workspace TEXT NOT NULL DEFAULT 'Default', source_application TEXT NULL, detection_confidence REAL NOT NULL DEFAULT 0, metadata_json TEXT NULL,
                html_content TEXT NULL, rtf_content TEXT NULL, ocr_text TEXT NULL);");
            db.Execute("CREATE TABLE settings (key TEXT PRIMARY KEY, value TEXT NOT NULL);");
            db.Execute("INSERT INTO settings (key, value) VALUES ('schema_version', '5');");
            db.Execute("INSERT INTO clipboard_items (content_type, title, binary_path, content_hash, created_at, last_copied_at, ocr_text) VALUES ('Image', 'image_with_old_ocr', 'images/test.png', 'h1', 1, 1, 'Old OCR text');");
        }

        var svc = NewService(folder: folder, fts: false);
        var images = svc.ImagesWithoutOcr(10);
        Assert.Equal(1, images.Count);
        Assert.Null(images[0].OcrText);
    }

    [Test]
    public void Parse_qr_and_ocr_text_separates_correctly()
    {
        var (qr1, ocr1) = ClipboardService.ParseQrAndOcrText("https://qrfy.com/");
        Assert.Equal("https://qrfy.com/", qr1);
        Assert.Null(ocr1);

        var (qr2, ocr2) = ClipboardService.ParseQrAndOcrText("https://qrfy.com/\r\n\r\nSome text in image\r\nLine 2");
        Assert.Equal("https://qrfy.com/", qr2);
        Assert.Equal("Some text in image\r\nLine 2", ocr2);

        var (qr3, ocr3) = ClipboardService.ParseQrAndOcrText("Just regular text in image\r\nNo QR");
        Assert.Null(qr3);
        Assert.Equal("Just regular text in image\r\nNo QR", ocr3);

        var (qr4, ocr4) = ClipboardService.ParseQrAndOcrText(null);
        Assert.Null(qr4);
        Assert.Null(ocr4);
    }

    [Test]
    public void LoadPayload_prioritizes_qr_for_plain_text()
    {
        var svc = NewService();
        var item = new ClipboardItem
        {
            Id = 1,
            Kind = ContentKind.Image,
            OcrText = "https://qrfy.com/\r\n\r\nSurrounding text buttons"
        };
        var payload = svc.LoadPayload(item, plainText: true);
        Assert.NotNull(payload);
        Assert.Equal("https://qrfy.com/", payload!.Text);
    }
}
