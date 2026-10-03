using ClipboardManager.Core.Models;
using ClipboardManager.Core.Services;

namespace ClipboardManager.Core.Tests;

/// <summary>Edge cases the Windows app hits in real use: parallel captures, FTS5-less SQLite, odd input.</summary>
public sealed class RobustnessTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "acm-rob-" + Guid.NewGuid().ToString("N"));
    private readonly List<ClipboardService> _services = new();

    private ClipboardService NewService(bool fts = true, AppSettings? settings = null, FakeClock? clock = null)
    {
        var svc = new ClipboardService(Path.Combine(_dir, _services.Count.ToString()), settings ?? new AppSettings(), clock ?? new FakeClock(), fts);
        _services.Add(svc);
        return svc;
    }

    public void Dispose()
    {
        foreach (var s in _services) s.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Test]
    public void Parallel_captures_are_safe()
    {
        // The app calls Capture from Task.Run, so several can overlap.
        var svc = NewService();
        Parallel.For(0, 200, i => svc.Capture(CapturedContent.FromText("parallel item " + (i % 50))));
        Assert.Equal(50, svc.Count());
        Assert.Equal(50, svc.Search("parallel").Count);
        Assert.Equal(200, svc.Search("parallel").Sum(x => x.CopyCount));
    }

    [Test]
    public void Parallel_capture_and_search_and_cleanup()
    {
        var svc = NewService();
        var errors = new System.Collections.Concurrent.ConcurrentBag<Exception>();
        var tasks = new List<Task>();
        for (int t = 0; t < 4; t++)
        {
            int id = t;
            tasks.Add(Task.Run(() =>
            {
                try
                {
                    for (int i = 0; i < 100; i++)
                    {
                        if (id == 0) svc.Capture(CapturedContent.FromText($"mix {i}"));
                        else if (id == 1) svc.Search("mix");
                        else if (id == 2) svc.CleanupExpired();
                        else svc.Search("type:text pinned:false");
                    }
                }
                catch (Exception ex) { errors.Add(ex); }
            }));
        }
        Task.WaitAll(tasks.ToArray());
        Assert.True(errors.IsEmpty, errors.FirstOrDefault()?.ToString());
        Assert.Equal(100, svc.Count());
    }

    [Test]
    public void Like_fallback_search_works_without_fts5()
    {
        var svc = NewService(fts: false);
        Assert.False(svc.FullTextSearchEnabled);
        svc.Capture(CapturedContent.FromText("docker compose up -d"));
        svc.Capture(CapturedContent.FromText("100% done_ok"));
        svc.Capture(CapturedContent.FromText("sk_live_51HxAbCdEfGhIjKlMnOpQrStUv"));
        Assert.Equal(1, svc.Search("compose").Count);
        Assert.Equal(1, svc.Search("100%").Count);          // % is escaped, not a wildcard
        Assert.Equal(1, svc.Search("done_ok").Count);       // _ is escaped
        Assert.Equal(0, svc.Search("AbCdEf").Count);        // secrets not searchable by content
        Assert.Equal(1, svc.Search("type:shell docker").Count);
    }

    [Test]
    public void Unicode_and_emoji_roundtrip()
    {
        var svc = NewService();
        var text = "Tiếng Việt có dấu 😀 — 日本語 \"quotes\" 'apos'";
        var (_, item) = svc.Capture(CapturedContent.FromText(text));
        Assert.Equal(text, svc.Get(item!.Id)!.TextContent);
        Assert.Equal(1, svc.Search("tieng viet").Count);
    }

    [Test]
    public void Too_large_text_is_skipped()
    {
        var svc = NewService(settings: new AppSettings { MaxTextChars = 100 });
        Assert.Equal(CaptureOutcome.SkippedTooLarge, svc.Capture(CapturedContent.FromText(new string('a', 101))).Outcome);
    }

    [Test]
    public void Files_are_stored_with_paths_and_title()
    {
        var svc = NewService();
        var (outcome, item) = svc.Capture(CapturedContent.FromFiles(new[] { @"C:\work\report.xlsx", @"C:\work\notes.txt" }));
        Assert.Equal(CaptureOutcome.Stored, outcome);
        Assert.Equal(ContentKind.Files, item!.Kind);
        Assert.True(item.Title.StartsWith("2 files"), item.Title);
        Assert.Equal(1, svc.Search("report").Count);
    }

    [Test]
    public void Same_image_twice_is_one_item_one_file()
    {
        var svc = NewService();
        var png = new byte[] { 1, 2, 3, 4, 5 };
        svc.Capture(CapturedContent.FromImage(png, 10, 10));
        var (outcome, item) = svc.Capture(CapturedContent.FromImage(png, 10, 10));
        Assert.Equal(CaptureOutcome.Duplicate, outcome);
        Assert.Equal(1, Directory.GetFiles(svc.ImagesFolder).Length);
        Assert.Equal(2, item!.CopyCount);
    }

    [Test]
    public void Recopying_a_secret_restarts_its_countdown()
    {
        var clock = new FakeClock();
        var svc = NewService(clock: clock);
        svc.Capture(CapturedContent.FromText("sk_live_51HxAbCdEfGhIjKlMnOpQrStUv"));
        clock.Advance(TimeSpan.FromMinutes(4));
        var (_, item) = svc.Capture(CapturedContent.FromText("sk_live_51HxAbCdEfGhIjKlMnOpQrStUv"));
        Assert.Equal(clock.Now + TimeSpan.FromMinutes(5), item!.ExpiresAt);
        clock.Advance(TimeSpan.FromMinutes(2));
        svc.CleanupExpired();
        Assert.Equal(1, svc.Count(), "still alive 6 min after first copy");
    }



    [Test]
    public void Data_survives_restart()
    {
        var folder = Path.Combine(_dir, "persist");
        using (var a = new ClipboardService(folder, new AppSettings(), new FakeClock()))
        {
            var (_, item) = a.Capture(CapturedContent.FromText("keep me"));
            a.TogglePin(item!);
        }
        using var b = new ClipboardService(folder, new AppSettings(), new FakeClock());
        var all = b.Search(null);
        Assert.Equal(1, all.Count);
        Assert.True(all[0].IsPinned);
        Assert.Equal(1, b.Search("keep").Count);
    }

    [Test]
    public void Window_position_is_saved_in_settings()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "pos.json");
        new AppSettings { QuickPasteLeft = 120.5, QuickPasteTop = 80 }.Save(path);
        var s = AppSettings.Load(path);
        Assert.Equal(120.5, s.QuickPasteLeft);
        Assert.Equal(80d, s.QuickPasteTop);
        Assert.Null(new AppSettings().QuickPasteLeft);
    }

    [Test]
    public void Excluded_app_matching_is_case_insensitive_and_ignores_blanks()
    {
        var svc = NewService(settings: new AppSettings { ExcludedApplications = new() { "  ", "keepass" } });
        Assert.True(svc.IsExcluded("KeePassXC"));
        Assert.False(svc.IsExcluded("notepad"));
        Assert.False(svc.IsExcluded(null));
    }
}
