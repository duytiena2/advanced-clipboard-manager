using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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
        SaveButtonText.Text = LocalizationService.Get("Snippet_Save");
        CancelButton.Content = LocalizationService.Get("Snippet_Cancel");
        InsertVarLabel.Text = LocalizationService.Get("Snippet_InsertVarLabel");
        InsertVarMenuText.Text = LocalizationService.Get("Snippet_InsertVarMenu");
        BracesHint.Text = LocalizationService.Get("Snippet_BracesHint");
        PreviewLabel.Text = LocalizationService.Get("Snippet_Preview");

        NameBox.Text = name;
        BodyBox.Text = body;

        PopulateVariables();
        UpdatePreview();

        BodyBox.TextChanged += (_, _) => UpdatePreview();
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

    private void PopulateVariables()
    {
        VariablesPanel.Children.Clear();
        var menu = new ContextMenu();
        var bodyMenu = new ContextMenu();

        var cut = new MenuItem { Command = ApplicationCommands.Cut };
        var copy = new MenuItem { Command = ApplicationCommands.Copy };
        var paste = new MenuItem { Command = ApplicationCommands.Paste };
        bodyMenu.Items.Add(cut);
        bodyMenu.Items.Add(copy);
        bodyMenu.Items.Add(paste);
        bodyMenu.Items.Add(new Separator());

        var insertSubMenu = new MenuItem { Header = LocalizationService.Get("Snippet_InsertVarMenu") };

        foreach (var (variable, description) in TemplateEngine.Variables)
        {
            var v = variable;
            var sample = GetSampleValue(variable);

            // 1. Clickable chip button
            var chip = new Button
            {
                Content = variable,
                Style = (Style)FindResource("VariableChip"),
                ToolTip = $"{LocalizationService.Get("Snippet_InsertVarTooltip", variable)}\n{LocalizationService.Get("Snippet_Example")}: {sample}"
            };
            chip.Click += (_, _) => InsertVariable(v);
            VariablesPanel.Children.Add(chip);

            // 2. Dropdown menu item
            var dropdownItem = new MenuItem
            {
                Header = $"{variable}   ({sample})",
                FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas, Segoe UI")
            };
            dropdownItem.Click += (_, _) => InsertVariable(v);
            menu.Items.Add(dropdownItem);

            // 3. Right-click context menu item
            var contextItem = new MenuItem
            {
                Header = $"{variable}   ({sample})",
                FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas, Segoe UI")
            };
            contextItem.Click += (_, _) => InsertVariable(v);
            insertSubMenu.Items.Add(contextItem);
        }

        bodyMenu.Items.Add(insertSubMenu);
        BodyBox.ContextMenu = bodyMenu;

        InsertVarMenuButton.Click += (_, _) =>
        {
            menu.PlacementTarget = InsertVarMenuButton;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        };
    }

    private void InsertVariable(string varText)
    {
        BodyBox.Focus();
        int selStart = BodyBox.SelectionStart;
        BodyBox.SelectedText = varText;
        BodyBox.SelectionStart = selStart + varText.Length;
        BodyBox.SelectionLength = 0;
        UpdatePreview();
    }

    private static string GetSampleValue(string variable)
    {
        return variable switch
        {
            "{date}" => DateTime.Now.ToString("d"),
            "{time}" => DateTime.Now.ToString("t"),
            "{datetime}" => DateTime.Now.ToString("g"),
            "{date:dd/MM/yyyy}" => DateTime.Now.ToString("dd/MM/yyyy"),
            "{clipboard}" => LocalizationService.Get("Snippet_SampleClipboard"),
            "{uuid}" => Guid.NewGuid().ToString()[..8] + "…",
            _ => TemplateEngine.Expand(variable)
        };
    }

    private void UpdatePreview()
    {
        var text = BodyBox.Text;
        if (TemplateEngine.HasVariables(text))
        {
            try
            {
                var preview = TemplateEngine.Expand(text, new TemplateContext
                {
                    Clipboard = () =>
                    {
                        try
                        {
                            var clip = Clipboard.GetText();
                            return string.IsNullOrEmpty(clip) ? $"[{LocalizationService.Get("Snippet_SampleClipboard")}]" : (clip.Length > 40 ? clip[..40] + "…" : clip);
                        }
                        catch { return $"[{LocalizationService.Get("Snippet_SampleClipboard")}]"; }
                    }
                });
                PreviewText.Text = preview;
                PreviewContainer.Visibility = Visibility.Visible;
            }
            catch
            {
                PreviewContainer.Visibility = Visibility.Collapsed;
            }
        }
        else
        {
            PreviewContainer.Visibility = Visibility.Collapsed;
        }
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
