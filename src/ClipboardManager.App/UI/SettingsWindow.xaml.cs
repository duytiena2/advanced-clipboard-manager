using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using ClipboardManager.App.Platform;
using ClipboardManager.Core.Classification;
using ClipboardManager.Core.Models;
using ClipboardManager.Core.Services;

namespace ClipboardManager.App.UI;

/// <summary>Settings editor (replaces hand-editing settings.json). Snippet changes are saved immediately; the rest on Save.</summary>
public partial class SettingsWindow : Window
{
    private readonly ClipboardService _svc;
    private readonly string _settingsPath;
    private readonly string? _ocrLanguage;
    private readonly ObservableCollection<RetentionRow> _retention = new();
    private readonly ObservableCollection<SnippetRow> _snippets = new();
    private string _hotkey;
    private bool _initializingLang;

    public static readonly DependencyProperty SidebarHeadingProperty =
        DependencyProperty.Register(nameof(SidebarHeading), typeof(string), typeof(SettingsWindow), new PropertyMetadata("Settings"));

    public static readonly DependencyProperty SidebarSubtitleProperty =
        DependencyProperty.Register(nameof(SidebarSubtitle), typeof(string), typeof(SettingsWindow), new PropertyMetadata(""));

    public string SidebarHeading
    {
        get => (string)GetValue(SidebarHeadingProperty);
        set => SetValue(SidebarHeadingProperty, value);
    }

    public string SidebarSubtitle
    {
        get => (string)GetValue(SidebarSubtitleProperty);
        set => SetValue(SidebarSubtitleProperty, value);
    }

    /// <summary>Raised after settings were saved, with the previous Quick Paste shortcut.</summary>
    internal event Action<string>? Saved;

    private static readonly (string Key, string Label)[] RetentionKinds =
    {
        (nameof(ContentKind.Text), "Text"),
        (nameof(ContentKind.Code), "Code (SQL, JSON, shell…)"),
        (nameof(ContentKind.Url), "Links"),
        (nameof(ContentKind.Email), "Email addresses"),
        (nameof(ContentKind.Phone), "Phone numbers"),
        (nameof(ContentKind.Number), "Numbers"),
        (nameof(ContentKind.Image), "Images"),
        (nameof(ContentKind.Files), "Files"),
        (nameof(ContentKind.Sensitive), "Secrets (keys, tokens)"),
        ("password", "Passwords"),
    };

    internal SettingsWindow(ClipboardService svc, string settingsPath, string? ocrLanguage)
    {
        _svc = svc;
        _settingsPath = settingsPath;
        _ocrLanguage = ocrLanguage;
        InitializeComponent();

        var s = svc.Settings;
        _hotkey = s.QuickPasteHotkey;
        HotkeyBox.Text = _hotkey;
        HotkeyBox.PreviewKeyDown += OnHotkeyKeyDown;
        CaptureBox.IsChecked = s.CaptureEnabled;
        MaxItemsBox.Text = s.MaxItems.ToString();
        OcrBox.IsChecked = s.OcrEnabled;

        TransparencyBox.IsChecked = s.EnableTransparency;
        OpacitySlider.Value = s.TransparencyOpacity;
        OpacityValueText.Text = $"{(int)(s.TransparencyOpacity * 100)}%";
        OpacitySlider.ValueChanged += (_, _) =>
        {
            OpacityValueText.Text = $"{(int)(OpacitySlider.Value * 100)}%";
        };

        DetectSensitiveBox.IsChecked = s.DetectSensitive;
        NeverPasswordsBox.IsChecked = s.NeverStorePasswords;
        NeverKeysBox.IsChecked = s.NeverStorePrivateKeys;
        ExcludedBox.Text = string.Join(Environment.NewLine, s.ExcludedApplications);
        EncryptBox.IsChecked = svc.IsEncrypted;

        foreach (var (key, label) in RetentionKinds)
            _retention.Add(new RetentionRow(key, LocalizationService.Get("Kind_" + key, label), s.RetentionMinutes.TryGetValue(key, out var m) ? m : 0));
        RetentionGrid.ItemsSource = _retention;


        SnippetsGrid.ItemsSource = _snippets;
        LoadSnippets();
        NewSnippetButton.Click += (_, _) => EditSnippet(null);
        EditSnippetButton.Click += (_, _) => { if (SnippetsGrid.SelectedItem is SnippetRow r) EditSnippet(r.Item); };
        SnippetsGrid.MouseDoubleClick += (_, _) => { if (SnippetsGrid.SelectedItem is SnippetRow r) EditSnippet(r.Item); };
        DeleteSnippetButton.Click += (_, _) => DeleteSnippet();

        _initializingLang = true;
        LanguageCombo.ItemsSource = LocalizationService.SupportedLanguages;
        LanguageCombo.DisplayMemberPath = "NativeName";
        var norm = LocalizationService.NormalizeLanguageCode(s.Language);
        LanguageCombo.SelectedItem = LocalizationService.SupportedLanguages.FirstOrDefault(l => l.Code == norm)
                                     ?? LocalizationService.SupportedLanguages[0];
        _initializingLang = false;

        LanguageCombo.SelectionChanged += (_, _) =>
        {
            if (_initializingLang) return;
            if (LanguageCombo.SelectedItem is LanguageOption opt)
            {
                LocalizationService.SetLanguage(opt.Code);
                ApplyLocalization();
            }
        };

        ApplyLocalization();

        SaveButton.Click += (_, _) => Save();
        CancelButton.Click += (_, _) => Close();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        };
    }

    /// <summary>Records the pressed combination, e.g. Ctrl+Shift+V.</summary>
    private void OnHotkeyKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        e.Handled = true;
        if (key is Key.Tab) { e.Handled = false; return; }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
            return;
        var parts = new List<string>();
        var mods = Keyboard.Modifiers;
        bool win = Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin);
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (win || mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        if (parts.Count == 0)
        {
            ErrorText.Text = "A shortcut needs Ctrl, Alt, Shift or Win.";
            return;
        }
        parts.Add(key.ToString());
        _hotkey = string.Join("+", parts);
        HotkeyBox.Text = _hotkey;
        ErrorText.Text = "";
    }

    private void LoadSnippets()
    {
        _snippets.Clear();
        foreach (var s in _svc.Snippets()) _snippets.Add(new SnippetRow(s));
    }

    private void EditSnippet(ClipboardItem? snippet)
    {
        var dialog = new SnippetDialog(_svc, snippet?.Title ?? "", snippet?.TextContent ?? "", snippet) { Owner = this, Topmost = false };
        if (dialog.ShowDialog() == true) LoadSnippets();
    }

    private void DeleteSnippet()
    {
        if (SnippetsGrid.SelectedItem is not SnippetRow row) return;
        var answer = MessageBox.Show(this,
            LocalizationService.Get("Dialog_DeleteSnippetConfirm", row.Title),
            LocalizationService.Get("Dialog_DeleteSnippetTitle"),
            MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
        if (answer != MessageBoxResult.OK) return;
        _svc.Delete(row.Item);
        LoadSnippets();
    }

    private void ApplyLocalization()
    {
        Title = LocalizationService.Get("Settings_Title");
        SidebarHeading = LocalizationService.Get("Settings_Heading");
        SidebarSubtitle = LocalizationService.Get("Settings_Subtitle");

        TabGeneralText.Text = LocalizationService.Get("Settings_Tab_General");
        TabPrivacyText.Text = LocalizationService.Get("Settings_Tab_Privacy");
        TabRetentionText.Text = LocalizationService.Get("Settings_Tab_Retention");
        TabSnippetsText.Text = LocalizationService.Get("Settings_Tab_Snippets");

        GeneralHeaderTitle.Text = LocalizationService.Get("Settings_General_Title");
        GeneralHeaderSubtitle.Text = LocalizationService.Get("Settings_General_Subtitle");
        PrivacyHeaderTitle.Text = LocalizationService.Get("Settings_Privacy_Title");
        PrivacyHeaderSubtitle.Text = LocalizationService.Get("Settings_Privacy_Subtitle");
        RetentionHeaderTitle.Text = LocalizationService.Get("Settings_Retention_Title");
        SnippetsHeaderTitle.Text = LocalizationService.Get("Settings_Snippets_Title");

        LanguageLabel.Text = LocalizationService.Get("Settings_Language");
        LanguageHint.Text = LocalizationService.Get("Settings_Language_Hint");
        HotkeyLabel.Text = LocalizationService.Get("Settings_Hotkey");
        HotkeyHint.Text = LocalizationService.Get("Settings_Hotkey_Hint");
        CaptureBox.Content = LocalizationService.Get("Settings_CaptureHistory");
        KeepAtMostLabel.Text = LocalizationService.Get("Settings_KeepAtMost");
        KeepAtMostHint.Text = LocalizationService.Get("Settings_ItemsCountHint");
        OcrBox.Content = LocalizationService.Get("Settings_Ocr");
        OcrHint.Text = _ocrLanguage is null
            ? LocalizationService.Get("Settings_OcrNoLang")
            : LocalizationService.Get("Settings_OcrRecognizes", _ocrLanguage);

        TransparencyHeading.Text = LocalizationService.Get("Settings_TransparencyHeading", "Widget & Transparency");
        TransparencyBox.Content = LocalizationService.Get("Settings_Transparency");
        OpacityLabel.Text = LocalizationService.Get("Settings_Opacity");
        TransparencyHint.Text = LocalizationService.Get("Settings_TransparencyHint");

        DetectSensitiveBox.Content = LocalizationService.Get("Settings_DetectSensitive");
        NeverPasswordsBox.Content = LocalizationService.Get("Settings_NeverPasswords");
        NeverKeysBox.Content = LocalizationService.Get("Settings_NeverKeys");
        ExcludedLabel.Text = LocalizationService.Get("Settings_ExcludedApps");
        ExcludedHint.Text = LocalizationService.Get("Settings_ExcludedHint");
        EncryptBox.Content = LocalizationService.Get("Settings_Encrypt");
        EncryptHint.Text = LocalizationService.Get("Settings_EncryptHint");

        RetentionHint.Text = LocalizationService.Get("Settings_RetentionHint");
        if (RetentionGrid.Columns.Count >= 2)
        {
            RetentionGrid.Columns[0].Header = LocalizationService.Get("Settings_Kind");
            RetentionGrid.Columns[1].Header = LocalizationService.Get("Settings_Minutes");
        }
        foreach (var r in _retention)
        {
            r.Label = LocalizationService.Get("Kind_" + r.Key, r.Key);
        }

        SnippetHint.Text = LocalizationService.Get("Settings_SnippetsHint",
            string.Join("  ", TemplateEngine.Variables.Select(v => v.Variable)));
        NewSnippetButtonText.Text = LocalizationService.Get("Settings_NewSnippet");
        EditSnippetButtonText.Text = LocalizationService.Get("Settings_EditSnippet");
        DeleteSnippetButtonText.Text = LocalizationService.Get("Settings_DeleteSnippet");
        if (SnippetsGrid.Columns.Count >= 2)
        {
            SnippetsGrid.Columns[0].Header = LocalizationService.Get("Settings_NameCol");
            SnippetsGrid.Columns[1].Header = LocalizationService.Get("Settings_TextCol");
        }

        SaveButtonText.Text = LocalizationService.Get("Settings_Save");
        CancelButton.Content = LocalizationService.Get("Settings_Cancel");
    }

    private void Save()
    {
        ErrorText.Text = "";
        RetentionGrid.CommitEdit();


        if (!WindowsHotkeyService.TryParse(_hotkey, out _, out _)) { Fail(0, "The shortcut is not valid."); return; }
        if (!int.TryParse(MaxItemsBox.Text.Trim(), out var maxItems) || maxItems < 0) { Fail(0, "\"Keep at most\" must be a whole number (0 = no limit)."); return; }
        var retention = new Dictionary<string, int>();
        foreach (var row in _retention)
        {
            if (!int.TryParse(row.Minutes.Trim(), out var minutes) || minutes < 0) { Fail(2, $"Retention for {row.Label} must be a whole number of minutes (0 = never)."); return; }
            retention[row.Key] = minutes;
        }

        // Encryption first: if it is cancelled or fails, nothing else has been changed yet.
        bool encrypt = EncryptBox.IsChecked == true;
        if (encrypt != _svc.IsEncrypted && !ChangeEncryption(encrypt)) return;

        var s = _svc.Settings;
        string oldHotkey = s.QuickPasteHotkey;
        if (LanguageCombo.SelectedItem is LanguageOption opt)
        {
            s.Language = opt.Code;
            LocalizationService.SetLanguage(s.Language);
        }
        s.QuickPasteHotkey = _hotkey;
        s.CaptureEnabled = CaptureBox.IsChecked == true;
        s.MaxItems = maxItems;
        s.OcrEnabled = OcrBox.IsChecked == true;
        s.EnableTransparency = false;
        s.TransparencyOpacity = 1.0;
        s.DetectSensitive = DetectSensitiveBox.IsChecked == true;
        s.NeverStorePasswords = NeverPasswordsBox.IsChecked == true;
        s.NeverStorePrivateKeys = NeverKeysBox.IsChecked == true;
        s.ExcludedApplications = ExcludedBox.Text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var kv in retention) s.RetentionMinutes[kv.Key] = kv.Value;

        try
        {
            s.Save(_settingsPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Fail(-1, "Could not save settings.json: " + ex.Message);
            return;
        }
        Saved?.Invoke(oldHotkey);
        Close();
    }

    private bool ChangeEncryption(bool on)
    {
        var question = on
            ? $"Encrypt all {_svc.Count()} items now? Only your Windows account on this PC will be able to read the history."
            : "Decrypt the history? It will be stored as plain data again.";
        if (MessageBox.Show(this, question, "Encryption", MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.OK) != MessageBoxResult.OK)
            return false;
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            _svc.SetEncryption(on);
            return true;
        }
        catch (Exception ex)
        {
            App.Log(ex);
            EncryptBox.IsChecked = _svc.IsEncrypted;
            Fail(1, "Encryption change failed, nothing was changed: " + ex.Message);
            return false;
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private void Fail(int tab, string message)
    {
        if (tab >= 0) Tabs.SelectedIndex = tab;
        ErrorText.Text = message;
    }

    private sealed class RetentionRow : INotifyPropertyChanged
    {
        private string _minutes;
        private string _label;

        public RetentionRow(string key, string label, int minutes)
        {
            Key = key;
            _label = label;
            _minutes = minutes.ToString();
        }

        public string Key { get; }
        public string Label
        {
            get => _label;
            set { _label = value; OnChanged(); }
        }

        public string Minutes
        {
            get => _minutes;
            set { _minutes = value; OnChanged(); OnChanged(nameof(Human)); }
        }

        public string Human => int.TryParse(_minutes, out var m) && m >= 0 ? Humanize.Minutes(m) : "?";

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private sealed class SnippetRow
    {
        public SnippetRow(ClipboardItem item) => Item = item;
        public ClipboardItem Item { get; }
        public string Title => Item.Title;
        public string Preview => ContentClassifier.MakeTitle(Item.TextContent, 80);
    }
}
