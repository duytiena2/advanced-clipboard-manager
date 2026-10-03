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
    private readonly ObservableCollection<RetentionRow> _retention = new();
    private readonly ObservableCollection<WorkspaceRule> _rules = new();
    private readonly ObservableCollection<SnippetRow> _snippets = new();
    private string _hotkey;

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
        InitializeComponent();

        var s = svc.Settings;
        _hotkey = s.QuickPasteHotkey;
        HotkeyBox.Text = _hotkey;
        HotkeyBox.PreviewKeyDown += OnHotkeyKeyDown;
        CaptureBox.IsChecked = s.CaptureEnabled;
        MaxItemsBox.Text = s.MaxItems.ToString();
        DefaultWorkspaceBox.Text = s.DefaultWorkspace;
        OcrBox.IsChecked = s.OcrEnabled;
        OcrHint.Text = ocrLanguage is null
            ? "No OCR language is installed. Add a language with OCR support in Windows Settings › Time & language › Language."
            : $"Recognizes {ocrLanguage}. Add more languages in Windows Settings › Time & language › Language (e.g. Vietnamese).";

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
            _retention.Add(new RetentionRow(key, label, s.RetentionMinutes.TryGetValue(key, out var m) ? m : 0));
        RetentionGrid.ItemsSource = _retention;

        foreach (var r in s.WorkspaceRules) _rules.Add(new WorkspaceRule(r.App, r.Workspace));
        RulesGrid.ItemsSource = _rules;
        AddRuleButton.Click += (_, _) =>
        {
            var rule = new WorkspaceRule("", "");
            _rules.Add(rule);
            RulesGrid.SelectedItem = rule;
            RulesGrid.CurrentCell = new System.Windows.Controls.DataGridCellInfo(rule, RulesGrid.Columns[0]);
            RulesGrid.BeginEdit();
        };
        RemoveRuleButton.Click += (_, _) => { if (RulesGrid.SelectedItem is WorkspaceRule r) _rules.Remove(r); };

        SnippetHint.Text = "Reusable text for Quick Paste (type:snippet). Variables: " +
                           string.Join("  ", TemplateEngine.Variables.Select(v => v.Variable)) + ". Changes here are saved immediately.";
        SnippetsGrid.ItemsSource = _snippets;
        LoadSnippets();
        NewSnippetButton.Click += (_, _) => EditSnippet(null);
        EditSnippetButton.Click += (_, _) => { if (SnippetsGrid.SelectedItem is SnippetRow r) EditSnippet(r.Item); };
        SnippetsGrid.MouseDoubleClick += (_, _) => { if (SnippetsGrid.SelectedItem is SnippetRow r) EditSnippet(r.Item); };
        DeleteSnippetButton.Click += (_, _) => DeleteSnippet();

        SaveButton.Click += (_, _) => Save();
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
        var answer = MessageBox.Show(this, $"Delete the snippet \"{row.Title}\"?", "Delete snippet",
            MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
        if (answer != MessageBoxResult.OK) return;
        _svc.Delete(row.Item);
        LoadSnippets();
    }

    private void Save()
    {
        ErrorText.Text = "";
        RetentionGrid.CommitEdit();
        RulesGrid.CommitEdit();

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
        s.QuickPasteHotkey = _hotkey;
        s.CaptureEnabled = CaptureBox.IsChecked == true;
        s.MaxItems = maxItems;
        s.DefaultWorkspace = string.IsNullOrWhiteSpace(DefaultWorkspaceBox.Text) ? "Default" : DefaultWorkspaceBox.Text.Trim();
        s.OcrEnabled = OcrBox.IsChecked == true;
        s.EnableTransparency = TransparencyBox.IsChecked == true;
        s.TransparencyOpacity = Math.Round(OpacitySlider.Value, 2);
        s.DetectSensitive = DetectSensitiveBox.IsChecked == true;
        s.NeverStorePasswords = NeverPasswordsBox.IsChecked == true;
        s.NeverStorePrivateKeys = NeverKeysBox.IsChecked == true;
        s.ExcludedApplications = ExcludedBox.Text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var kv in retention) s.RetentionMinutes[kv.Key] = kv.Value;
        s.WorkspaceRules = _rules.Where(r => !string.IsNullOrWhiteSpace(r.App) && !string.IsNullOrWhiteSpace(r.Workspace))
            .Select(r => new WorkspaceRule(r.App.Trim(), r.Workspace.Trim())).ToList();

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

        public RetentionRow(string key, string label, int minutes)
        {
            Key = key;
            Label = label;
            _minutes = minutes.ToString();
        }

        public string Key { get; }
        public string Label { get; }

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
