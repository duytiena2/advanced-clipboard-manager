using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using ClipboardManager.Core.Models;
using ClipboardManager.Core.Services;
using ClipboardManager.Mac.Platform;

namespace ClipboardManager.Mac.Views;

public partial class SettingsWindow : Window
{
    private readonly ClipboardService? _svc;
    private readonly string _settingsPath;
    private readonly Action? _onSaved;

    private readonly ObservableCollection<string> _excludedApps;

    public SettingsWindow() : this(null, "")
    {
    }

    public SettingsWindow(ClipboardService? svc, string settingsPath, Action? onSaved = null)
    {
        InitializeComponent();

        _svc = svc;
        _settingsPath = settingsPath;
        _onSaved = onSaved;

        var s = _svc?.Settings ?? new AppSettings();

        // Language
        var languages = LocalizationService.SupportedLanguages;
        LanguageCombo.ItemsSource = languages.Select(l => $"{l.NativeName} ({l.EnglishName})").ToList();
        int langIdx = languages.ToList().FindIndex(l => l.Code.Equals(s.Language, StringComparison.OrdinalIgnoreCase));
        LanguageCombo.SelectedIndex = langIdx >= 0 ? langIdx : 0;

        // Hotkey & options
        HotkeyBox.Text = s.QuickPasteHotkey;
        CaptureBox.IsChecked = s.CaptureEnabled;
        StartupBox.IsChecked = MacStartupRegistration.IsEnabled();

        // Privacy
        EncryptBox.IsChecked = s.EncryptDatabase;
        DetectSensitiveBox.IsChecked = s.DetectSensitive;
        OcrBox.IsChecked = s.OcrEnabled;

        // Excluded apps
        _excludedApps = new ObservableCollection<string>(s.ExcludedApplications ?? new List<string>());
        ExcludedList.ItemsSource = _excludedApps;
        AddExcludedButton.Click += (_, _) =>
        {
            var app = NewExcludedBox.Text?.Trim();
            if (!string.IsNullOrEmpty(app) && !_excludedApps.Contains(app))
            {
                _excludedApps.Add(app);
                NewExcludedBox.Text = "";
            }
        };
        RemoveExcludedButton.Click += (_, _) =>
        {
            if (ExcludedList.SelectedItem is string item)
            {
                _excludedApps.Remove(item);
            }
        };

        // Retention
        var ret = s.RetentionMinutes ?? AppSettings.DefaultRetention();
        RetText.Text = (ret.TryGetValue(nameof(ContentKind.Text), out int t) ? t : 1440).ToString();
        RetCode.Text = (ret.TryGetValue(nameof(ContentKind.Code), out int c) ? c : 10080).ToString();
        RetImage.Text = (ret.TryGetValue(nameof(ContentKind.Image), out int img) ? img : 60).ToString();
        RetUrl.Text = (ret.TryGetValue(nameof(ContentKind.Url), out int u) ? u : 10080).ToString();
        RetSensitive.Text = (ret.TryGetValue(nameof(ContentKind.Sensitive), out int sec) ? sec : 5).ToString();

        // Advanced
        MaxItemsBox.Text = s.MaxItems.ToString();
        NeverStorePasswordsBox.IsChecked = s.NeverStorePasswords;
        NeverStorePrivateKeysBox.IsChecked = s.NeverStorePrivateKeys;
        BlurSensitiveBox.IsChecked = s.BlurSensitive;

        // Buttons
        CancelButton.Click += (_, _) => Close();
        SaveButton.Click += (_, _) => SaveSettings();
    }

    private void SaveSettings()
    {
        if (_svc == null) return;
        var s = _svc.Settings;

        // Language
        int selectedLangIdx = LanguageCombo.SelectedIndex;
        if (selectedLangIdx >= 0 && selectedLangIdx < LocalizationService.SupportedLanguages.Count)
        {
            s.Language = LocalizationService.SupportedLanguages[selectedLangIdx].Code;
            LocalizationService.SetLanguage(s.Language);
        }

        // Hotkey & flags
        s.QuickPasteHotkey = string.IsNullOrWhiteSpace(HotkeyBox.Text) ? "Cmd+Shift+V" : HotkeyBox.Text.Trim();
        s.CaptureEnabled = CaptureBox.IsChecked == true;
        s.EncryptDatabase = EncryptBox.IsChecked == true;
        s.DetectSensitive = DetectSensitiveBox.IsChecked == true;
        s.OcrEnabled = OcrBox.IsChecked == true;

        MacStartupRegistration.SetEnabled(StartupBox.IsChecked == true);

        // Excluded apps
        s.ExcludedApplications = _excludedApps.ToList();

        // Retention
        var ret = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (int.TryParse(RetText.Text, out int rt)) ret[nameof(ContentKind.Text)] = rt;
        if (int.TryParse(RetCode.Text, out int rc)) ret[nameof(ContentKind.Code)] = rc;
        if (int.TryParse(RetImage.Text, out int ri)) ret[nameof(ContentKind.Image)] = ri;
        if (int.TryParse(RetUrl.Text, out int ru)) ret[nameof(ContentKind.Url)] = ru;
        if (int.TryParse(RetSensitive.Text, out int rs)) ret[nameof(ContentKind.Sensitive)] = rs;
        s.RetentionMinutes = ret;

        // Advanced
        if (int.TryParse(MaxItemsBox.Text, out int max) && max > 0) s.MaxItems = max;
        s.NeverStorePasswords = NeverStorePasswordsBox.IsChecked == true;
        s.NeverStorePrivateKeys = NeverStorePrivateKeysBox.IsChecked == true;
        s.BlurSensitive = BlurSensitiveBox.IsChecked == true;

        // Persist
        s.Save(_settingsPath);
        _onSaved?.Invoke();
        Close();
    }
}
