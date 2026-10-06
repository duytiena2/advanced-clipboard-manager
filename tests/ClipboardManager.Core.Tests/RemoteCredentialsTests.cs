using ClipboardManager.Core.Classification;
using ClipboardManager.Core.Models;
using ClipboardManager.Core.Services;

namespace ClipboardManager.Core.Tests;

public sealed class RemoteCredentialsTests
{
    private readonly ContentClassifier _classifier = new();

    [Test]
    public void UltraViewer_Hyphen_Separated()
    {
        string input = "39 850 251 - 4461";
        Assert.True(RemoteCredentials.TryParse(input, out var creds));
        Assert.NotNull(creds);
        Assert.Equal("UltraViewer", creds!.Provider);
        Assert.Equal("39 850 251", creds.Id);
        Assert.Equal("39850251", creds.NormalizedId);
        Assert.Equal("4461", creds.Password);
        Assert.Equal("UltraViewer: 39 850 251 | Pass: 4461", creds.ToFormattedTitle());
    }

    [Test]
    public void UltraViewer_Two_Lines()
    {
        string input = "39 850 251\n4461";
        Assert.True(RemoteCredentials.TryParse(input, out var creds));
        Assert.NotNull(creds);
        Assert.Equal("39 850 251", creds!.Id);
        Assert.Equal("4461", creds.Password);
    }

    [Test]
    public void UltraViewer_Slash_Separated()
    {
        string input = "39 850 251 / 4461";
        Assert.True(RemoteCredentials.TryParse(input, out var creds));
        Assert.NotNull(creds);
        Assert.Equal("39 850 251", creds!.Id);
        Assert.Equal("4461", creds.Password);
    }

    [Test]
    public void UltraViewer_Vietnamese_Labels()
    {
        string input = "ID của bạn: 39 850 251, Mật khẩu: 4461";
        Assert.True(RemoteCredentials.TryParse(input, out var creds));
        Assert.NotNull(creds);
        Assert.Equal("39 850 251", creds!.Id);
        Assert.Equal("4461", creds.Password);
    }

    [Test]
    public void UltraViewer_English_Labels()
    {
        string input = "Your ID 39 982 012\nPassword 4337";
        Assert.True(RemoteCredentials.TryParse(input, out var creds));
        Assert.NotNull(creds);
        Assert.Equal("39 982 012", creds!.Id);
        Assert.Equal("4337", creds.Password);
    }

    [Test]
    public void UltraViewer_Crop_Small()
    {
        string input = "Your ID 528\nPassword 1384";
        Assert.True(RemoteCredentials.TryParse(input, out var creds));
        Assert.NotNull(creds);
        Assert.Equal("528", creds!.Id);
        Assert.Equal("1384", creds.Password);
    }

    [Test]
    public void UltraViewer_Full_Ocr_Text()
    {
        string input = "UltraViewer 6.5 - Free\nCho phép điều khiển\nIO của bạn 39 850 251\nMatkhẩu 4461\nĐiều khiển máy tính khác";
        Assert.True(RemoteCredentials.TryParse(input, out var creds));
        Assert.NotNull(creds);
        Assert.Equal("39 850 251", creds!.Id);
        Assert.Equal("4461", creds.Password);
    }

    [Test]
    public void TeamViewer_Format()
    {
        string input = "TeamViewer ID: 1 234 567 890 Pass: abc123";
        Assert.True(RemoteCredentials.TryParse(input, out var creds));
        Assert.NotNull(creds);
        Assert.Equal("TeamViewer", creds!.Provider);
        Assert.Equal("1 234 567 890", creds.Id);
        Assert.Equal("abc123", creds.Password);
    }

    [Test]
    public void Classifier_Identifies_UltraViewer()
    {
        var res = _classifier.ClassifyText("39 850 251 - 4461");
        Assert.Equal(ContentKind.Text, res.Kind);
        Assert.Equal("ultraviewer", res.Subtype);

        string label = ContentClassifier.GetClassificationLabel(res.Kind, res.Subtype);
        Assert.Equal("UltraViewer", label);

        string title = ContentClassifier.MakeTitle("39 850 251 - 4461");
        Assert.Equal("UltraViewer: 39 850 251 | Pass: 4461", title);
    }

    [Test]
    public void Remote_Transforms_Work()
    {
        string input = "ID: 39 850 251 Pass: 4461";
        Assert.Equal("39 850 251", TextTransforms.Apply("remote-id", input));
        Assert.Equal("4461", TextTransforms.Apply("remote-pass", input));
        Assert.Equal("39 850 251\t4461", TextTransforms.Apply("remote-tab", input));
    }
}
