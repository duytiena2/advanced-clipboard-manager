using ClipboardManager.Core.Models;
using ClipboardManager.Core.Platform;
using ClipboardManager.Core.Search;
using ClipboardManager.Core.Services;

namespace ClipboardManager.Core.Tests;

internal sealed class FakeClock : IClock
{
    public DateTimeOffset Now { get; set; } = new(2026, 10, 2, 9, 0, 0, TimeSpan.Zero);
    public void Advance(TimeSpan t) => Now += t;
}

/// <summary>Each test gets a fresh temp data folder + SQLite database.</summary>
public sealed class ServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "acm-tests-" + Guid.NewGuid().ToString("N"));
    private readonly FakeClock _clock = new();
    private readonly AppSettings _settings = new();
    private readonly ClipboardService _svc;

    public ServiceTests() => _svc = new ClipboardService(_dir, _settings, _clock);

    public void Dispose()
    {
        _svc.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private ClipboardItem Copy(string text, string? app = null)
    {
        var (outcome, item) = _svc.Capture(CapturedContent.FromText(text, app));
        Assert.True(outcome is CaptureOutcome.Stored or CaptureOutcome.Duplicate, "outcome " + outcome);
        return item!;
    }

    [Test]
    public void Fts5_is_available_in_test_environment()
    {
        // On Windows and macOS, SQLite is bundled with FTS5. On Linux CI, system libsqlite3 may lack FTS5 and gracefully fall back.
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
            Assert.True(_svc.FullTextSearchEnabled, "FTS5 missing");
    }

    [Test]
    public void Stores_history_newest_first()
    {
        Copy("first");
        _clock.Advance(TimeSpan.FromSeconds(1));
        Copy("second");
        var items = _svc.Search(null);
        Assert.Equal(2, items.Count);
        Assert.Equal("second", items[0].TextContent);
    }

    [Test]
    public void Duplicates_are_merged_and_counted()
    {
        Copy("docker ps");
        _clock.Advance(TimeSpan.FromMinutes(1));
        var (outcome, item) = _svc.Capture(CapturedContent.FromText("docker ps"));
        Assert.Equal(CaptureOutcome.Duplicate, outcome);
        Assert.Equal(2, item!.CopyCount);
        Assert.Equal(1, _svc.Count());
        Assert.Equal(2, _svc.Get(item.Id)!.CopyCount);
    }

    [Test]
    public void Duplicate_moves_item_to_top()
    {
        Copy("aaa");
        _clock.Advance(TimeSpan.FromSeconds(1));
        Copy("bbb");
        _clock.Advance(TimeSpan.FromSeconds(1));
        Copy("aaa");
        Assert.Equal("aaa", _svc.Search(null)[0].TextContent);
    }

    [Test]
    public void Full_text_search_with_prefix()
    {
        Copy("docker compose up -d");
        Copy("docker logs backend");
        Copy("SELECT * FROM users;");
        Assert.Equal(2, _svc.Search("dock").Count);
        Assert.Equal(1, _svc.Search("users").Count);
        Assert.Equal(1, _svc.Search("docker logs").Count);
    }

    [Test]
    public void Search_ignores_diacritics()
    {
        Copy("Xin chào thế giới");
        Assert.Equal(1, _svc.Search("chao").Count);
    }

    [Test]
    public void Search_with_fts_operators_does_not_throw()
    {
        Copy("a AND b OR (c)");
        _svc.Search("AND OR NOT \" * ( )");
        _svc.Search("\"unbalanced");
    }

    [Test]
    public void Filters_by_type_and_pinned()
    {
        Copy("SELECT 1 FROM dual");
        var url = Copy("https://github.com/a/b");
        _svc.TogglePin(url);
        Assert.Equal(1, _svc.Search("type:sql").Count);
        Assert.Equal(1, _svc.Search("type:url").Count);
        Assert.Equal(1, _svc.Search("pinned:true").Count);
        Assert.Equal("https://github.com/a/b", _svc.Search("pinned:true")[0].TextContent);
    }

    [Test]
    public void Pinned_items_listed_first()
    {
        var a = Copy("pin me");
        _clock.Advance(TimeSpan.FromSeconds(5));
        Copy("newer");
        _svc.TogglePin(a);
        Assert.Equal("pin me", _svc.Search(null)[0].TextContent);
    }

    [Test]
    public void Expiration_uses_kind_retention()
    {
        var text = Copy("hello world");
        Assert.Equal(_clock.Now + TimeSpan.FromDays(1), text.ExpiresAt);
        var code = Copy("SELECT * FROM t");
        Assert.Equal(_clock.Now + TimeSpan.FromDays(7), code.ExpiresAt);
        var secret = Copy("sk_live_51HxAbCdEfGhIjKlMnOpQrStUv");
        Assert.Equal(_clock.Now + TimeSpan.FromMinutes(5), secret.ExpiresAt);
        var pw = Copy("password=hunter2");
        Assert.Equal(_clock.Now + TimeSpan.FromMinutes(1), pw.ExpiresAt);
    }

    [Test]
    public void Cleanup_removes_expired_but_keeps_pinned()
    {
        var keep = Copy("keep this note");
        _svc.TogglePin(keep);
        Copy("temporary note");
        Copy("sk_live_51HxAbCdEfGhIjKlMnOpQrStUv");
        _clock.Advance(TimeSpan.FromMinutes(6));
        _svc.CleanupExpired();
        Assert.Equal(2, _svc.Count(), "only secret expired after 6 min");
        _clock.Advance(TimeSpan.FromDays(2));
        _svc.CleanupExpired();
        Assert.Equal(1, _svc.Count());
        Assert.Equal("keep this note", _svc.Search(null)[0].TextContent);
    }

    [Test]
    public void Unpin_restores_expiration()
    {
        var a = Copy("note");
        _svc.TogglePin(a);
        Assert.Null(_svc.Get(a.Id)!.ExpiresAt);
        _svc.TogglePin(a);
        Assert.NotNull(_svc.Get(a.Id)!.ExpiresAt);
    }

    [Test]
    public void Sensitive_items_are_masked_and_not_searchable_by_content()
    {
        var s = Copy("sk_live_51HxAbCdEfGhIjKlMnOpQrStUv");
        Assert.True(s.IsSensitive);
        Assert.False(s.Title.Contains("AbCdEf"), "title must be masked");
        Assert.Equal(0, _svc.Search("51HxAbCdEf").Count);
        Assert.Equal(1, _svc.Search("sensitive:true").Count);
    }

    [Test]
    public void Private_keys_are_never_stored_by_default()
    {
        var (outcome, _) = _svc.Capture(CapturedContent.FromText("-----BEGIN RSA PRIVATE KEY-----\nMIIE\n-----END RSA PRIVATE KEY-----"));
        Assert.Equal(CaptureOutcome.SkippedByPrivacyRule, outcome);
        Assert.Equal(0, _svc.Count());
    }

    [Test]
    public void Paused_capture_stores_nothing()
    {
        _settings.CaptureEnabled = false;
        Assert.Equal(CaptureOutcome.SkippedPaused, _svc.Capture(CapturedContent.FromText("x")).Outcome);
        Assert.Equal(0, _svc.Count());
    }

    [Test]
    public void Excluded_application_is_skipped()
    {
        Assert.Equal(CaptureOutcome.SkippedExcludedApp, _svc.Capture(CapturedContent.FromText("x", "KeePassXC")).Outcome);
        Assert.Equal(CaptureOutcome.Stored, _svc.Capture(CapturedContent.FromText("x", "Code")).Outcome);
    }

    [Test]
    public void Empty_text_is_skipped() =>
        Assert.Equal(CaptureOutcome.SkippedEmpty, _svc.Capture(CapturedContent.FromText("   ")).Outcome);

    [Test]
    public void Images_are_stored_on_disk_and_deleted_with_item()
    {
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 };
        var (outcome, item) = _svc.Capture(CapturedContent.FromImage(png, 1920, 1080));
        Assert.Equal(CaptureOutcome.Stored, outcome);
        var path = _svc.FullBinaryPath(item!)!;
        Assert.True(File.Exists(path), "image file written");
        Assert.Equal("Image 1920 × 1080", item!.Title);
        Assert.Equal(_clock.Now + TimeSpan.FromHours(1), item.ExpiresAt);
        _svc.Delete(item);
        Assert.False(File.Exists(path), "image file removed");
    }

    [Test]
    public void Max_items_evicts_oldest_unpinned()
    {
        _settings.MaxItems = 3;
        var pinned = Copy("pinned one");
        _svc.TogglePin(pinned);
        for (int i = 0; i < 5; i++)
        {
            _clock.Advance(TimeSpan.FromSeconds(1));
            Copy("item " + i);
        }
        var all = _svc.Search(null);
        Assert.Equal(4, all.Count, "3 unpinned + 1 pinned");
        Assert.True(all.Any(x => x.TextContent == "pinned one"));
        Assert.False(all.Any(x => x.TextContent == "item 0"));
    }

    [Test]
    public void Clear_history_keeps_pinned()
    {
        var p = Copy("pinned");
        _svc.TogglePin(p);
        Copy("a"); Copy("b");
        _svc.ClearHistory();
        Assert.Equal(1, _svc.Count());
    }

    [Test]
    public void Merge_items_with_separators()
    {
        var items = new[] { Copy("Hello"), Copy("How are you?\n"), Copy("Today?") };
        Assert.Equal("Hello" + Environment.NewLine + "How are you?" + Environment.NewLine + "Today?", MergeService.Merge(items, MergeSeparator.NewLine));
        Assert.Equal("Hello, How are you?, Today?", MergeService.Merge(items, MergeSeparator.Comma));
        Assert.Equal("Hello | How are you? | Today?", MergeService.Merge(items, MergeSeparator.Custom, " | "));
    }

    [Test]
    public void Settings_roundtrip()
    {
        var path = Path.Combine(_dir, "settings.json");
        var s = new AppSettings { MaxItems = 123, QuickPasteHotkey = "Ctrl+Alt+V", QuickPasteListRatio = 0.40 };
        s.ExcludedApplications.Add("BankApp");
        s.Save(path);
        var loaded = AppSettings.Load(path);
        Assert.Equal(123, loaded.MaxItems);
        Assert.Equal("Ctrl+Alt+V", loaded.QuickPasteHotkey);
        Assert.Equal(0.40, loaded.QuickPasteListRatio);
        Assert.True(loaded.ExcludedApplications.Contains("BankApp"));
        File.WriteAllText(path, "{ broken");
        Assert.Equal(5000, AppSettings.Load(path).MaxItems);
    }

    [Test]
    public void Performance_10k_items_search_under_100ms()
    {
        for (int i = 0; i < 2_000; i++)
        {
            _clock.Advance(TimeSpan.FromMilliseconds(10));
            _svc.Capture(CapturedContent.FromText($"item number {i} docker container {i % 97} lorem"));
        }
        _svc.Search("docker"); // warm-up
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r = _svc.Search("container 42");
        sw.Stop();
        Assert.True(r.Count > 0);
        Assert.True(sw.ElapsedMilliseconds < 500, $"search took {sw.ElapsedMilliseconds} ms");
    }
}

public sealed class QueryParserTests
{
    [Test]
    public void Parses_terms_and_filters()
    {
        var q = SearchQuery.Parse("docker \"compose up\" type:shell pinned:true after:2026-09-01 before:2026-10-01");
        Assert.Equal(2, q.Terms.Count);
        Assert.Equal("compose up", q.Terms[1]);
        Assert.Equal("shell", q.Subtype);
        Assert.Equal(true, q.Pinned);
        Assert.NotNull(q.After);
        Assert.NotNull(q.Before);
    }

    [Test]
    public void Type_maps_to_kind_when_possible()
    {
        Assert.Equal(ContentKind.Code, SearchQuery.Parse("type:code").Kind);
        Assert.Equal(ContentKind.Url, SearchQuery.Parse("type:link").Kind);
        Assert.Equal(ContentKind.Image, SearchQuery.Parse("type:image").Kind);
        Assert.Equal(ContentKind.Image, SearchQuery.Parse("type:images").Kind);
        Assert.Equal(ContentKind.Snippet, SearchQuery.Parse("type:snippet").Kind);
        Assert.Equal(ContentKind.Snippet, SearchQuery.Parse("type:snippets").Kind);
        Assert.Equal(ContentKind.Files, SearchQuery.Parse("type:files").Kind);
        Assert.Equal("sql", SearchQuery.Parse("type:SQL").Subtype);
    }

    [Test]
    public void Urls_and_paths_stay_free_text()
    {
        var q = SearchQuery.Parse("https://x.y C:\\temp");
        Assert.Equal(2, q.Terms.Count);
        Assert.True(q.Kind is null && q.Subtype is null);
    }

    [Test]
    public void Empty_query() => Assert.True(SearchQuery.Parse("  ").IsEmpty);

    [Test]
    public void Filter_chips_extracted_and_formatted()
    {
        var (chips, remaining) = SearchFilterChip.ExtractFilters("docker type:sql pinned:true");
        Assert.Equal("docker", remaining);
        Assert.Equal(2, chips.Count);
        Assert.Equal("SQL", chips[0].Label);
        Assert.Equal("type:sql", chips[0].RawSyntax);
        Assert.Equal("Pinned", chips[1].Label);

        string combined = SearchFilterChip.Combine(chips, remaining);
        var q = SearchQuery.Parse(combined);
        Assert.Equal(1, q.Terms.Count);
        Assert.Equal("docker", q.Terms[0]);
        Assert.Equal("sql", q.Subtype);
        Assert.Equal(true, q.Pinned);
    }

    [Test]
    public void Filter_chips_single_token_auto_converts()
    {
        var (chips, remaining) = SearchFilterChip.ExtractFilters("type:sql");
        Assert.Equal("", remaining);
        Assert.Equal(1, chips.Count);
        Assert.Equal("SQL", chips[0].Label);

        Assert.Equal("Images", SearchFilterChip.FormatLabel("type", "image"));
        Assert.Equal("Images", SearchFilterChip.FormatLabel("type", "images"));
        Assert.Equal("Snippets", SearchFilterChip.FormatLabel("type", "snippet"));
        Assert.Equal("Snippets", SearchFilterChip.FormatLabel("type", "snippets"));
    }
}

public sealed class HumanizeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 9, 0, 0, TimeSpan.Zero);

    [Test]
    public void Ago_formats()
    {
        Assert.Equal("just now", Humanize.Ago(Now.AddSeconds(-10), Now));
        Assert.Equal("2 min ago", Humanize.Ago(Now.AddMinutes(-2), Now));
        Assert.Equal("3 h ago", Humanize.Ago(Now.AddHours(-3), Now));
        Assert.Equal("yesterday", Humanize.Ago(Now.AddHours(-30), Now));
        Assert.Equal("5 days ago", Humanize.Ago(Now.AddDays(-5), Now));
    }

    [Test]
    public void ExpiresIn_formats()
    {
        Assert.Equal("never expires", Humanize.ExpiresIn(null, Now));
        Assert.Equal("expires in 04:32", Humanize.ExpiresIn(Now.AddMinutes(4).AddSeconds(32), Now));
        Assert.Equal("expires in 5 days", Humanize.ExpiresIn(Now.AddDays(4.5), Now));
        Assert.Equal("expired", Humanize.ExpiresIn(Now.AddSeconds(-1), Now));
    }
}
