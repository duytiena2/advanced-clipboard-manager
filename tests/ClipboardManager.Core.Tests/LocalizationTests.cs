using System.IO;
using ClipboardManager.Core.Services;

namespace ClipboardManager.Core.Tests;

public sealed class LocalizationTests
{
    [Test]
    public void SupportedLanguages_Contains_10_Languages()
    {
        var langs = LocalizationService.SupportedLanguages;
        Assert.Equal(10, langs.Count);

        var codes = langs.Select(l => l.Code).ToList();
        Assert.True(codes.Contains("en"));
        Assert.True(codes.Contains("vi"));
        Assert.True(codes.Contains("zh"));
        Assert.True(codes.Contains("ja"));
        Assert.True(codes.Contains("ko"));
        Assert.True(codes.Contains("es"));
        Assert.True(codes.Contains("fr"));
        Assert.True(codes.Contains("de"));
        Assert.True(codes.Contains("ru"));
        Assert.True(codes.Contains("pt"));
    }

    [Test]
    public void NormalizeLanguageCode_Works()
    {
        Assert.Equal("vi", LocalizationService.NormalizeLanguageCode("vi-VN"));
        Assert.Equal("vi", LocalizationService.NormalizeLanguageCode("VI"));
        Assert.Equal("zh", LocalizationService.NormalizeLanguageCode("zh-CN"));
        Assert.Equal("ja", LocalizationService.NormalizeLanguageCode("ja-JP"));
        Assert.Equal("ko", LocalizationService.NormalizeLanguageCode("ko-KR"));
        Assert.Equal("es", LocalizationService.NormalizeLanguageCode("es-ES"));
        Assert.Equal("fr", LocalizationService.NormalizeLanguageCode("fr-FR"));
        Assert.Equal("de", LocalizationService.NormalizeLanguageCode("de-DE"));
        Assert.Equal("ru", LocalizationService.NormalizeLanguageCode("ru-RU"));
        Assert.Equal("pt", LocalizationService.NormalizeLanguageCode("pt-BR"));
        Assert.Equal("en", LocalizationService.NormalizeLanguageCode("en-US"));
        Assert.Equal("en", LocalizationService.NormalizeLanguageCode(""));
        Assert.Equal("en", LocalizationService.NormalizeLanguageCode(null));
        Assert.Equal("en", LocalizationService.NormalizeLanguageCode("unknown"));
    }

    [Test]
    public void Translations_Exist_For_All_Languages()
    {
        foreach (var lang in LocalizationService.SupportedLanguages)
        {
            LocalizationService.SetLanguage(lang.Code);
            var title = LocalizationService.Get("Settings_Title");
            var quickPaste = LocalizationService.Get("QuickPaste_Title");
            var trayQuickPaste = LocalizationService.Get("Tray_QuickPaste");

            Assert.True(!string.IsNullOrWhiteSpace(title));
            Assert.True(!string.IsNullOrWhiteSpace(quickPaste));
            Assert.True(!string.IsNullOrWhiteSpace(trayQuickPaste));
        }

        // Reset to English
        LocalizationService.SetLanguage("en");
    }

    [Test]
    public void Vietnamese_Translations_Correct()
    {
        LocalizationService.SetLanguage("vi");
        Assert.Equal("Trình quản lý Clipboard — Cài đặt", LocalizationService.Get("Settings_Title"));
        Assert.Equal("Ngôn ngữ giao diện", LocalizationService.Get("Settings_Language"));
        Assert.Equal("Dán nhanh", LocalizationService.Get("QuickPaste_Title"));
        Assert.Equal("10 mục", LocalizationService.Get("QuickPaste_ItemsCount", 10));
        Assert.Equal("Bấm để chèn biến tự động:", LocalizationService.Get("Snippet_InsertVarLabel"));
        Assert.Equal("Chèn biến {date}", LocalizationService.Get("Snippet_InsertVarTooltip", "{date}"));

        LocalizationService.SetLanguage("en");
    }

    [Test]
    public void LanguageChanged_Event_Fired()
    {
        LocalizationService.SetLanguage("en");
        bool eventFired = false;
        Action handler = () => eventFired = true;

        LocalizationService.LanguageChanged += handler;
        try
        {
            LocalizationService.SetLanguage("vi");
            Assert.True(eventFired);
        }
        finally
        {
            LocalizationService.LanguageChanged -= handler;
            LocalizationService.SetLanguage("en");
        }
    }

    [Test]
    public void Fallback_To_English_Or_Key()
    {
        LocalizationService.SetLanguage("vi");
        // Unknown key falls back to key itself
        Assert.Equal("NonExistentKey123", LocalizationService.Get("NonExistentKey123"));
        LocalizationService.SetLanguage("en");
    }

    [Test]
    public void Settings_Headings_And_Subtitles_Localized()
    {
        LocalizationService.SetLanguage("en");
        Assert.Equal("Settings", LocalizationService.Get("Settings_Heading"));
        Assert.Equal("System configuration", LocalizationService.Get("Settings_Subtitle"));
        Assert.Equal("General", LocalizationService.Get("Settings_General_Title"));
        Assert.Equal("Keyboard shortcuts, history recording, and application interface", LocalizationService.Get("Settings_General_Subtitle"));

        LocalizationService.SetLanguage("vi");
        Assert.Equal("Cài đặt", LocalizationService.Get("Settings_Heading"));
        Assert.Equal("Cấu hình hệ thống", LocalizationService.Get("Settings_Subtitle"));
        Assert.Equal("Chung", LocalizationService.Get("Settings_General_Title"));
        Assert.Equal("Cài đặt phím tắt, ghi nhớ lịch sử và giao diện ứng dụng", LocalizationService.Get("Settings_General_Subtitle"));

        LocalizationService.SetLanguage("en");
    }
}
