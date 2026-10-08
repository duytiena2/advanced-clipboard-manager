using ClipboardManager.Core.Classification;
using ClipboardManager.Core.Models;

namespace ClipboardManager.Core.Tests;

public sealed class ClassifierTests
{
    private readonly ContentClassifier _c = new();

    private void Expect(string input, ContentKind kind, string subtype)
    {
        var r = _c.ClassifyText(input);
        Assert.Equal(kind, r.Kind, $"kind for «{input}»");
        Assert.Equal(subtype, r.Subtype, $"subtype for «{input}»");
    }

    [Test] public void Sql_select() => Expect("SELECT id, name\nFROM users\nWHERE id = 10;", ContentKind.Code, "sql");
    [Test] public void Sql_lowercase_update() => Expect("update users set status = 'active' where id = 3", ContentKind.Code, "sql");
    [Test] public void Url_github() => Expect("https://github.com/example/project", ContentKind.Url, "github");
    [Test] public void Url_plain() => Expect("https://example.org/docs/quickstart", ContentKind.Url, "url");
    [Test] public void Url_www() => Expect("www.example.com/path", ContentKind.Url, "url");
    [Test] public void Email() => Expect("hello@example.com", ContentKind.Email, "email");
    [Test] public void Phone_vn() => Expect("+84 90 123 4567", ContentKind.Phone, "phone");
    [Test] public void Number() => Expect("1,234,567.89", ContentKind.Number, "number");
    [Test] public void Ip() => Expect("192.168.1.10:8080", ContentKind.Text, "ip");
    [Test] public void Json() => Expect("{ \"users\": [ { \"id\": 10 } ] }", ContentKind.Code, "json");
    [Test] public void Json_invalid_is_not_json() => Assert.True(_c.ClassifyText("{ not json }").Subtype != "json");
    [Test] public void Xml() => Expect("<note><to>User</to></note>", ContentKind.Code, "xml");
    [Test] public void Shell_docker() => Expect("docker compose up -d", ContentKind.Code, "shell");
    [Test] public void Shell_git() => Expect("git checkout -b feature/name", ContentKind.Code, "shell");
    [Test] public void Yaml() => Expect("services:\n  app:\n    image: example/app:1.4\n    ports:\n      - \"80:80\"", ContentKind.Code, "yaml");
    [Test] public void Log() => Expect("2026-10-02 10:00:01 ERROR connection refused\n2026-10-02 10:00:02 INFO retrying", ContentKind.Text, "log");
    [Test] public void Code_js() => Expect("const add = (a, b) => {\n  return a + b;\n};", ContentKind.Code, "code");
    [Test] public void Markdown() => Expect("# Title\n\n- item one\n- item two\n\nSee [docs](https://x.y)", ContentKind.Text, "markdown");
    [Test] public void Plain_text() => Expect("Hello, how are you?", ContentKind.Text, "plain");
    [Test] public void Vietnamese_text_is_plain() => Expect("Xin chào, hôm nay bạn thế nào?", ContentKind.Text, "plain");

    [Test]
    public void NormalizeText_cases()
    {
        Assert.Equal("cskh@realtyholdings.vn", ContentClassifier.NormalizeText(" cskh@realtyholdings.vn ", ContentKind.Email));
        Assert.Equal("https://example.com", ContentClassifier.NormalizeText("  https://example.com  ", ContentKind.Url));
        Assert.Equal("11311", ContentClassifier.NormalizeText(" 11311 ", ContentKind.Number));
        Assert.Equal("hello world", ContentClassifier.NormalizeText("  hello world  ", ContentKind.Text));
        Assert.Equal("    var x = 1;", ContentClassifier.NormalizeText("    var x = 1;  ", ContentKind.Code));
        Assert.Equal("line1\nline2", ContentClassifier.NormalizeText("line1\r\nline2\r\n\r\n", ContentKind.Text));
    }

    // Sensitive
    [Test] public void Stripe_key() => Expect("sk_live_51HxAbCdEfGhIjKlMnOpQrStUv", ContentKind.Sensitive, "api-key");
    [Test] public void Github_token() => Expect("ghp_" + new string('a', 20) + "B1c2D3e4F5g6H7i8J9k0", ContentKind.Sensitive, "token");
    [Test] public void Aws_key() => Expect("AKIAIOSFODNN7EXAMPLE", ContentKind.Sensitive, "aws-key");
    [Test] public void Jwt() => Expect("eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.dozjgNryP4J3jVmNHl0w5N_XgL0n3I9PlFUP0THsR8U", ContentKind.Sensitive, "jwt");
    [Test] public void Private_key() => Expect("-----BEGIN OPENSSH PRIVATE KEY-----\nb3BlbnNzaC1rZXk\n-----END OPENSSH PRIVATE KEY-----", ContentKind.Sensitive, "private-key");
    [Test] public void Bearer_header() => Expect("Authorization: Bearer abcdefghijklmnopqrstuvwxyz123456", ContentKind.Sensitive, "auth-header");
    [Test] public void Connection_string() => Expect("Server=db;Database=app;User Id=sa;Password=Secr3t!;", ContentKind.Sensitive, "connection-string");
    [Test] public void Db_url_with_password() => Expect("postgres://admin:p4ss@db.internal:5432/app", ContentKind.Sensitive, "connection-string");
    [Test] public void Password_assignment() => Expect("password=hunter2", ContentKind.Sensitive, "password");
    [Test] public void Random_secret() => Expect("Zk3pQ9vX2mR7tL5wN8bY4cH6jF1dS0aG", ContentKind.Sensitive, "token");
    [Test] public void Normal_url_not_sensitive() => Assert.False(_c.ClassifyText("https://github.com/example/project").IsSensitive);
    [Test] public void Long_word_lowercase_not_sensitive() => Assert.False(_c.ClassifyText("internationalizationlocalization").IsSensitive);

    [Test]
    public void Mask_keeps_prefix_and_suffix()
    {
        var m = ContentClassifier.Mask("sk_live_51HxAbCdEfGhIjKl");
        Assert.True(m.StartsWith("sk_li") && m.EndsWith("IjKl") && m.Contains('•'), m);
        Assert.False(m.Contains("AbCdEf"), m);
    }

    [Test]
    public void Title_is_first_non_empty_line_and_truncated()
    {
        Assert.Equal("SELECT *", ContentClassifier.MakeTitle("\n\n  SELECT *\nFROM t"));
        var longTitle = ContentClassifier.MakeTitle(new string('x', 500), 50);
        Assert.Equal(50, longTitle.Length);
        Assert.True(longTitle.EndsWith('…'));
    }

    [Test]
    public void Image_and_files_classification()
    {
        Assert.Equal(ContentKind.Image, _c.Classify(CapturedContent.FromImage(new byte[] { 1 }, 1, 1)).Kind);
        Assert.Equal("files", _c.Classify(CapturedContent.FromFiles(new[] { "a", "b" })).Subtype);
    }

    [Test]
    public void Classification_headers_match_user_spec()
    {
        // SQL: \uE71D SQL
        Assert.Equal("\uE71D SQL", ContentClassifier.GetClassificationHeader(ContentKind.Code, "sql", "SELECT * FROM users"));

        // JavaScript: \uE943 JavaScript
        Assert.Equal("\uE943 JavaScript", ContentClassifier.GetClassificationHeader(ContentKind.Code, "code", "const user = await fetchUser();"));

        // GitHub URL: \uE71B GitHub URL
        Assert.Equal("\uE71B GitHub URL", ContentClassifier.GetClassificationHeader(ContentKind.Url, "github", "https://github.com/duytiena2/clipboard"));

        // JSON: \uE943 JSON
        Assert.Equal("\uE943 JSON", ContentClassifier.GetClassificationHeader(ContentKind.Code, "json", "{\n  \"name\": \"John\"\n}"));

        // Screenshot: \uEB9F Screenshot
        Assert.Equal("\uEB9F Screenshot", ContentClassifier.GetClassificationHeader(ContentKind.Image, "png"));

        // Shell: \uE756 Shell
        Assert.Equal("\uE756 Shell", ContentClassifier.GetClassificationHeader(ContentKind.Code, "shell", "docker compose up -d"));

        // Markdown: \uE8A5 Markdown
        Assert.Equal("\uE8A5 Markdown", ContentClassifier.GetClassificationHeader(ContentKind.Text, "markdown"));

        // Log: \uE9D9 Log
        Assert.Equal("\uE9D9 Log", ContentClassifier.GetClassificationHeader(ContentKind.Text, "log"));

        // Email: \uE715 Email
        Assert.Equal("\uE715 Email", ContentClassifier.GetClassificationHeader(ContentKind.Email, "email"));

        // Sensitive: \uE72E API Key
        Assert.Equal("\uE72E API Key", ContentClassifier.GetClassificationHeader(ContentKind.Sensitive, "api-key"));
    }

    [Test]
    public void Detect_code_languages()
    {
        Assert.Equal("JavaScript", ContentClassifier.DetectCodeLanguage("const user = { name: 'Alice' };"));
        Assert.Equal("JavaScript", ContentClassifier.DetectCodeLanguage("const add = (a, b) => a + b;"));
        Assert.Equal("Python", ContentClassifier.DetectCodeLanguage("def calculate_total(items):\n    return sum(items)"));
        Assert.Equal("C#", ContentClassifier.DetectCodeLanguage("using System;\npublic class App {}"));
        Assert.Equal("HTML", ContentClassifier.DetectCodeLanguage("<div class=\"container\"><span>Text</span></div>"));
        Assert.Equal("CSS", ContentClassifier.DetectCodeLanguage(".btn { display: flex; margin: 10px; }"));
        Assert.Equal("Go", ContentClassifier.DetectCodeLanguage("package main\nfunc main() {}"));
        Assert.Equal("Rust", ContentClassifier.DetectCodeLanguage("fn main() {\n    let mut x = 5;\n}"));
    }

    [Test]
    public void ClipboardItem_classification_header_property()
    {
        var item = new ClipboardItem
        {
            Kind = ContentKind.Code,
            Subtype = "sql",
            TextContent = "SELECT id, name FROM users",
            Title = "SELECT id, name FROM users"
        };
        Assert.Equal("\uE71D SQL", item.ClassificationHeader);
        Assert.Equal("SQL", item.ClassificationLabel);
        Assert.Equal("\uE71D", item.ClassificationIcon);
        Assert.Equal("SQL", item.DisplayType);
    }

    [Test]
    public void ClipboardItem_four_layer_properties()
    {
        var imgItem = new ClipboardItem
        {
            Kind = ContentKind.Image,
            Subtype = "png",
            Title = "Image 1103 × 593",
            CopyCount = 2,
            LastCopiedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        };
        Assert.Equal("🖼", imgItem.TypeIcon);
        Assert.Equal("PNG", imgItem.TypeBadge);
        Assert.True(imgItem.HasCopyCount);
        Assert.Equal("2×", imgItem.CopyCountText);

        var txtItem = new ClipboardItem
        {
            Kind = ContentKind.Text,
            Subtype = "plain",
            Title = "khi mà kéo đủ rộng thì tự bung đầy đủ",
            CopyCount = 1,
            IsPinned = true,
            LastCopiedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
        };
        Assert.Equal("T", txtItem.TypeIcon);
        Assert.Equal("Plain", txtItem.TypeBadge);
        Assert.False(txtItem.HasCopyCount);
        Assert.Equal("📌", txtItem.PinGlyph);
    }
}
