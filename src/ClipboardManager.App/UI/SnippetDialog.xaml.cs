using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using ClipboardManager.Core.Models;
using ClipboardManager.Core.Services;

namespace ClipboardManager.App.UI;

/// <summary>Create or edit a snippet. Enter inserts a newline in the text; Ctrl+Enter saves.</summary>
public partial class SnippetDialog : Window
{
    private readonly ClipboardService _svc;
    private readonly ClipboardItem? _existing;

    public ClipboardItem? Saved { get; private set; }

    internal SnippetDialog(ClipboardService svc, string name, string body, ClipboardItem? existing = null)
    {
        _svc = svc;
        _existing = existing;
        InitializeComponent();
        Title = existing is null ? LocalizationService.Get("Snippet_TitleNew") : LocalizationService.Get("Snippet_TitleEdit");
        NameLabel.Text = LocalizationService.Get("Snippet_Name");
        BodyLabel.Text = LocalizationService.Get("Snippet_Text");
        SaveButton.Content = LocalizationService.Get("Snippet_Save");
        CancelButton.Content = LocalizationService.Get("Snippet_Cancel");
        NameBox.Text = name;
        BodyBox.Text = body;
        VariablesText.Text = "Variables: " + string.Join("   ", TemplateEngine.Variables.Select(v => v.Variable)) + "   ·   {{ }} for literal braces";

        SaveButton.Click += (_, _) => Save();
        CancelButton.Click += (_, _) => Close();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control) { Save(); e.Handled = true; }
        };
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    private void Save()
    {
        try
        {
            Saved = _svc.SaveSnippet(NameBox.Text, BodyBox.Text, _existing);
            DialogResult = true;
        }
        catch (ArgumentException ex)
        {
            ErrorText.Text = ex.Message.Split(" (Parameter")[0];
        }
    }
}
