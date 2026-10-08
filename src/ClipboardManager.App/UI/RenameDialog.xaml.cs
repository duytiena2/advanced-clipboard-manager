using System;
using System.Windows;
using System.Windows.Input;
using ClipboardManager.Core.Classification;
using ClipboardManager.Core.Models;
using ClipboardManager.Core.Services;

namespace ClipboardManager.App.UI;

/// <summary>Dialog to set a custom title / alias for any clipboard history item.</summary>
public partial class RenameDialog : Window
{
    private readonly ClipboardService _svc;
    private readonly ClipboardItem _item;

    public RenameDialog(ClipboardService svc, ClipboardItem item)
    {
        _svc = svc;
        _item = item;
        InitializeComponent();

        Title = LocalizationService.Get("Rename_Title");
        HeaderTitle.Text = LocalizationService.Get("Rename_Title");
        PromptLabel.Text = LocalizationService.Get("Rename_Prompt");
        HintLabel.Text = LocalizationService.Get("Rename_Hint");
        SaveButtonText.Text = LocalizationService.Get("Rename_Save");
        CancelButton.Content = LocalizationService.Get("Rename_Cancel");
        ResetButton.Content = LocalizationService.Get("Rename_Reset");

        NameBox.Text = item.Title;

        var preview = !string.IsNullOrWhiteSpace(item.TextContent)
            ? ContentClassifier.MakeTitle(item.TextContent, 80)
            : item.Title;
        PreviewText.Text = preview;

        SaveButton.Click += (_, _) => Save();
        ResetButton.Click += (_, _) => ResetToDefault();
        CancelButton.Click += (_, _) => Close();

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
            {
                Save();
                e.Handled = true;
            }
        };

        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    private void ResetToDefault()
    {
        NameBox.Text = "";
        Save();
    }

    private void Save()
    {
        var text = NameBox.Text.Trim();
        try
        {
            if (_item.Kind == ContentKind.Snippet && string.IsNullOrWhiteSpace(text))
            {
                ShowError("Snippets must have a name.");
                return;
            }

            _svc.Rename(_item, string.IsNullOrWhiteSpace(text) ? null : text);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void ShowError(string msg)
    {
        ErrorText.Text = msg;
        ErrorText.Visibility = Visibility.Visible;
    }
}
