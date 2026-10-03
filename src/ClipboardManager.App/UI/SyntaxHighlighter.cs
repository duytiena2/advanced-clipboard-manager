using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace ClipboardManager.App.UI;

/// <summary>
/// Fast, zero-dependency syntax highlighter for WPF RichTextBox / FlowDocument.
/// Supports SQL, JSON, XML, YAML, Shell, and general code languages.
/// </summary>
public static class SyntaxHighlighter
{
    private static readonly FontFamily MonoFont = new("Cascadia Mono, Consolas, Courier New");

    private static readonly SolidColorBrush KeywordBrush = FreezeBrush("#0550AE");   // GitHub / VS Code blue
    private static readonly SolidColorBrush StringBrush = FreezeBrush("#A31515");    // Brick red / amber
    private static readonly SolidColorBrush NumberBrush = FreezeBrush("#098658");    // Green
    private static readonly SolidColorBrush CommentBrush = FreezeBrush("#6E7781");   // Gray italic
    private static readonly SolidColorBrush TagBrush = FreezeBrush("#116329");       // Dark green
    private static readonly SolidColorBrush AttributeBrush = FreezeBrush("#0550AE"); // Blue
    private static readonly SolidColorBrush LiteralBrush = FreezeBrush("#CF222E");   // Magenta / Red
    private static readonly SolidColorBrush PunctuationBrush = FreezeBrush("#57606A");// Muted slate
    private static readonly SolidColorBrush DefaultBrush = FreezeBrush("#1F2328");   // Dark text
    private static readonly SolidColorBrush VariableBrush = FreezeBrush("#953800");  // Amber / orange

    private static SolidColorBrush FreezeBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    private static readonly HashSet<string> SqlKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT", "FROM", "WHERE", "INSERT", "INTO", "UPDATE", "SET", "DELETE",
        "JOIN", "INNER", "LEFT", "RIGHT", "OUTER", "CROSS", "FULL", "ON",
        "GROUP", "BY", "ORDER", "HAVING", "LIMIT", "OFFSET", "UNION", "ALL",
        "AS", "AND", "OR", "NOT", "IN", "IS", "NULL", "LIKE", "BETWEEN",
        "EXISTS", "CASE", "WHEN", "THEN", "ELSE", "END", "CREATE", "TABLE",
        "INDEX", "VIEW", "TRIGGER", "DATABASE", "DROP", "ALTER", "ADD",
        "PRIMARY", "KEY", "FOREIGN", "REFERENCES", "DEFAULT", "CHECK",
        "CONSTRAINT", "WITH", "VALUES", "DISTINCT", "COUNT", "SUM", "AVG",
        "MIN", "MAX", "COALESCE", "CAST", "DESC", "ASC", "TOP", "DISTINCTROW",
        "PROCEDURE", "FUNCTION", "EXEC", "EXECUTE", "BEGIN", "TRANSACTION",
        "COMMIT", "ROLLBACK", "TRUNCATE", "REPLACE", "PRAGMA", "VIRTUAL"
    };

    private static readonly HashSet<string> CodeKeywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "async", "await", "base", "bool", "break", "byte", "case", "catch",
        "char", "checked", "class", "const", "continue", "decimal", "default", "delegate",
        "do", "double", "else", "enum", "event", "explicit", "extern", "false", "finally",
        "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface",
        "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator",
        "out", "override", "params", "private", "protected", "public", "readonly", "ref",
        "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static", "string",
        "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong",
        "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while",
        "function", "def", "let", "var", "import", "export", "from", "package", "type",
        "fn", "pub", "impl", "trait", "mut", "self", "None", "True", "False", "lambda", "yield",
        "print", "println", "fmt", "nil", "val"
    };

    private static readonly HashSet<string> ShellCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "git", "docker", "docker-compose", "kubectl", "npm", "npx", "pnpm", "yarn",
        "pip", "pip3", "python", "python3", "dotnet", "cargo", "go", "ssh", "scp",
        "curl", "wget", "cd", "ls", "mkdir", "rm", "cp", "mv", "chmod", "chown",
        "cat", "grep", "tail", "head", "systemctl", "apt", "apt-get", "brew", "winget",
        "choco", "helm", "terraform", "az", "aws", "gcloud", "ping", "sudo", "echo",
        "find", "sed", "awk", "tar", "zip", "unzip", "kill", "ps", "top", "export"
    };

    public static FlowDocument CreateDocument(string text, string language)
    {
        var doc = new FlowDocument
        {
            PagePadding = new Thickness(0),
            FontFamily = MonoFont,
            FontSize = 13
        };

        var paragraph = new Paragraph
        {
            Margin = new Thickness(0),
            LineHeight = 20
        };
        doc.Blocks.Add(paragraph);

        if (string.IsNullOrEmpty(text)) return doc;

        var lang = (language ?? "").ToLowerInvariant();
        switch (lang)
        {
            case "sql":
                HighlightSql(paragraph, text);
                break;
            case "json":
                HighlightJson(paragraph, text);
                break;
            case "xml" or "html":
                HighlightXml(paragraph, text);
                break;
            case "yaml" or "yml":
                HighlightYaml(paragraph, text);
                break;
            case "shell" or "bash" or "sh" or "cmd" or "powershell":
                HighlightShell(paragraph, text);
                break;
            default:
                HighlightGeneralCode(paragraph, text);
                break;
        }

        return doc;
    }

    private static void AddRun(Paragraph p, string text, SolidColorBrush brush, bool isBold = false, bool isItalic = false)
    {
        if (string.IsNullOrEmpty(text)) return;
        var run = new Run(text)
        {
            Foreground = brush,
            FontWeight = isBold ? FontWeights.SemiBold : FontWeights.Normal,
            FontStyle = isItalic ? FontStyles.Italic : FontStyles.Normal
        };
        p.Inlines.Add(run);
    }

    private static void AddLineBreak(Paragraph p)
    {
        p.Inlines.Add(new LineBreak());
    }

    private static void HighlightSql(Paragraph p, string text)
    {
        int i = 0;
        int len = text.Length;

        while (i < len)
        {
            char c = text[i];

            if (c == '\r')
            {
                i++;
                continue;
            }
            if (c == '\n')
            {
                AddLineBreak(p);
                i++;
                continue;
            }

            // Single line comment -- ...
            if (c == '-' && i + 1 < len && text[i + 1] == '-')
            {
                int start = i;
                while (i < len && text[i] != '\n' && text[i] != '\r') i++;
                AddRun(p, text[start..i], CommentBrush, isItalic: true);
                continue;
            }

            // Multi line comment /* ... */
            if (c == '/' && i + 1 < len && text[i + 1] == '*')
            {
                int start = i;
                i += 2;
                while (i < len && !(text[i - 1] == '*' && text[i] == '/'))
                {
                    if (text[i] == '\n') { AddRun(p, text[start..i], CommentBrush, isItalic: true); AddLineBreak(p); start = i + 1; }
                    i++;
                }
                if (i < len) i++; // consume '/'
                if (start < i) AddRun(p, text[start..i], CommentBrush, isItalic: true);
                continue;
            }

            // String '...'
            if (c == '\'' || c == '"')
            {
                char quote = c;
                int start = i;
                i++;
                while (i < len && text[i] != quote && text[i] != '\n')
                {
                    if (text[i] == '\\' && i + 1 < len) i++;
                    i++;
                }
                if (i < len && text[i] == quote) i++;
                AddRun(p, text[start..i], StringBrush);
                continue;
            }

            // Numbers
            if (char.IsDigit(c))
            {
                int start = i;
                while (i < len && (char.IsDigit(text[i]) || text[i] == '.' || text[i] == 'x' || text[i] == 'X')) i++;
                AddRun(p, text[start..i], NumberBrush);
                continue;
            }

            // Words / Identifiers / Keywords
            if (char.IsLetter(c) || c == '_' || c == '@')
            {
                int start = i;
                while (i < len && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;
                string word = text[start..i];
                if (SqlKeywords.Contains(word))
                    AddRun(p, word, KeywordBrush, isBold: true);
                else
                    AddRun(p, word, DefaultBrush);
                continue;
            }

            // Operators & Punctuation
            if ("=<>!+-*/%&|^;(),.".Contains(c))
            {
                AddRun(p, c.ToString(), PunctuationBrush);
                i++;
                continue;
            }

            // Other / whitespace
            int wsStart = i;
            while (i < len && text[i] != '\n' && text[i] != '\r' && !char.IsLetterOrDigit(text[i]) && !"'-/*\"=<>!+%&|^;(),.".Contains(text[i])) i++;
            if (i > wsStart)
                AddRun(p, text[wsStart..i], DefaultBrush);
            else
            {
                AddRun(p, c.ToString(), DefaultBrush);
                i++;
            }
        }
    }

    private static void HighlightJson(Paragraph p, string text)
    {
        int i = 0;
        int len = text.Length;

        while (i < len)
        {
            char c = text[i];

            if (c == '\r') { i++; continue; }
            if (c == '\n') { AddLineBreak(p); i++; continue; }

            // Strings / Keys
            if (c == '"')
            {
                int start = i;
                i++;
                while (i < len && text[i] != '"' && text[i] != '\n')
                {
                    if (text[i] == '\\' && i + 1 < len) i++;
                    i++;
                }
                if (i < len && text[i] == '"') i++;
                string s = text[start..i];

                // Check if followed by colon (ignoring whitespace) -> it's a property key!
                int look = i;
                while (look < len && (text[look] == ' ' || text[look] == '\t')) look++;
                bool isKey = look < len && text[look] == ':';

                if (isKey)
                    AddRun(p, s, KeywordBrush, isBold: true);
                else
                    AddRun(p, s, StringBrush);
                continue;
            }

            // Numbers
            if (char.IsDigit(c) || (c == '-' && i + 1 < len && char.IsDigit(text[i + 1])))
            {
                int start = i;
                i++;
                while (i < len && (char.IsDigit(text[i]) || text[i] == '.' || text[i] == 'e' || text[i] == 'E' || text[i] == '+' || text[i] == '-')) i++;
                AddRun(p, text[start..i], NumberBrush);
                continue;
            }

            // Literals (true, false, null)
            if (char.IsLetter(c))
            {
                int start = i;
                while (i < len && char.IsLetter(text[i])) i++;
                string word = text[start..i];
                if (word is "true" or "false" or "null")
                    AddRun(p, word, LiteralBrush, isBold: true);
                else
                    AddRun(p, word, DefaultBrush);
                continue;
            }

            // Punctuation { } [ ] : ,
            if ("{}[],:".Contains(c))
            {
                AddRun(p, c.ToString(), PunctuationBrush, isBold: c is '{' or '}' or '[' or ']');
                i++;
                continue;
            }

            AddRun(p, c.ToString(), DefaultBrush);
            i++;
        }
    }

    private static void HighlightXml(Paragraph p, string text)
    {
        int i = 0;
        int len = text.Length;

        while (i < len)
        {
            char c = text[i];
            if (c == '\r') { i++; continue; }
            if (c == '\n') { AddLineBreak(p); i++; continue; }

            // Comment <!-- ... -->
            if (c == '<' && i + 3 < len && text[i + 1] == '!' && text[i + 2] == '-' && text[i + 3] == '-')
            {
                int start = i;
                i += 4;
                while (i + 2 < len && !(text[i] == '-' && text[i + 1] == '-' && text[i + 2] == '>'))
                {
                    if (text[i] == '\n') { AddRun(p, text[start..i], CommentBrush, isItalic: true); AddLineBreak(p); start = i + 1; }
                    i++;
                }
                if (i + 2 < len) i += 3;
                if (start < i) AddRun(p, text[start..i], CommentBrush, isItalic: true);
                continue;
            }

            // Tags <tag ... >
            if (c == '<')
            {
                AddRun(p, "<", PunctuationBrush);
                i++;
                if (i < len && (text[i] == '/' || text[i] == '?'))
                {
                    AddRun(p, text[i].ToString(), PunctuationBrush);
                    i++;
                }

                // Tag name
                int tagStart = i;
                while (i < len && !char.IsWhiteSpace(text[i]) && text[i] != '>' && text[i] != '/' && text[i] != '?') i++;
                if (i > tagStart) AddRun(p, text[tagStart..i], TagBrush, isBold: true);

                // Inside tag attributes until '>'
                while (i < len && text[i] != '>')
                {
                    if (text[i] == '\r') { i++; continue; }
                    if (text[i] == '\n') { AddLineBreak(p); i++; continue; }

                    if (text[i] == '/' || text[i] == '?')
                    {
                        AddRun(p, text[i].ToString(), PunctuationBrush);
                        i++;
                        continue;
                    }

                    if (text[i] == '"' || text[i] == '\'')
                    {
                        char quote = text[i];
                        int sStart = i++;
                        while (i < len && text[i] != quote && text[i] != '\n') i++;
                        if (i < len && text[i] == quote) i++;
                        AddRun(p, text[sStart..i], StringBrush);
                        continue;
                    }

                    if (text[i] == '=')
                    {
                        AddRun(p, "=", PunctuationBrush);
                        i++;
                        continue;
                    }

                    if (char.IsLetter(text[i]) || text[i] == '_' || text[i] == ':')
                    {
                        int aStart = i;
                        while (i < len && (char.IsLetterOrDigit(text[i]) || text[i] == '_' || text[i] == '-' || text[i] == ':')) i++;
                        AddRun(p, text[aStart..i], AttributeBrush);
                        continue;
                    }

                    AddRun(p, text[i].ToString(), DefaultBrush);
                    i++;
                }

                if (i < len && text[i] == '>')
                {
                    AddRun(p, ">", PunctuationBrush);
                    i++;
                }
                continue;
            }

            // Content outside tags
            int textStart = i;
            while (i < len && text[i] != '<' && text[i] != '\n' && text[i] != '\r') i++;
            if (i > textStart)
                AddRun(p, text[textStart..i], DefaultBrush);
            else if (i < len && text[i] != '<' && text[i] != '\n' && text[i] != '\r')
            {
                AddRun(p, text[i].ToString(), DefaultBrush);
                i++;
            }
        }
    }

    private static void HighlightYaml(Paragraph p, string text)
    {
        int i = 0;
        int len = text.Length;

        while (i < len)
        {
            char c = text[i];
            if (c == '\r') { i++; continue; }
            if (c == '\n') { AddLineBreak(p); i++; continue; }

            // Comment # ...
            if (c == '#')
            {
                int start = i;
                while (i < len && text[i] != '\n' && text[i] != '\r') i++;
                AddRun(p, text[start..i], CommentBrush, isItalic: true);
                continue;
            }

            // String '...' or "..."
            if (c == '\'' || c == '"')
            {
                char quote = c;
                int start = i++;
                while (i < len && text[i] != quote && text[i] != '\n') i++;
                if (i < len && text[i] == quote) i++;
                AddRun(p, text[start..i], StringBrush);
                continue;
            }

            // Key: identifier followed by ':'
            if (char.IsLetter(c) || c == '_' || c == '-')
            {
                int start = i;
                while (i < len && (char.IsLetterOrDigit(text[i]) || text[i] == '_' || text[i] == '-')) i++;
                string word = text[start..i];

                if (i < len && text[i] == ':')
                {
                    AddRun(p, word, KeywordBrush, isBold: true);
                    AddRun(p, ":", PunctuationBrush);
                    i++;
                    continue;
                }

                if (word is "true" or "false" or "yes" or "no" or "null" or "on" or "off")
                    AddRun(p, word, LiteralBrush, isBold: true);
                else
                    AddRun(p, word, DefaultBrush);
                continue;
            }

            // Numbers
            if (char.IsDigit(c))
            {
                int start = i;
                while (i < len && (char.IsDigit(text[i]) || text[i] == '.')) i++;
                AddRun(p, text[start..i], NumberBrush);
                continue;
            }

            AddRun(p, c.ToString(), PunctuationBrush);
            i++;
        }
    }

    private static void HighlightShell(Paragraph p, string text)
    {
        int i = 0;
        int len = text.Length;

        while (i < len)
        {
            char c = text[i];
            if (c == '\r') { i++; continue; }
            if (c == '\n') { AddLineBreak(p); i++; continue; }

            // Comment # ...
            if (c == '#')
            {
                int start = i;
                while (i < len && text[i] != '\n' && text[i] != '\r') i++;
                AddRun(p, text[start..i], CommentBrush, isItalic: true);
                continue;
            }

            // Strings '...' or "..."
            if (c == '\'' || c == '"')
            {
                char quote = c;
                int start = i++;
                while (i < len && text[i] != quote && text[i] != '\n')
                {
                    if (text[i] == '\\' && i + 1 < len) i++;
                    i++;
                }
                if (i < len && text[i] == quote) i++;
                AddRun(p, text[start..i], StringBrush);
                continue;
            }

            // Flags -flag or --flag
            if (c == '-' && i + 1 < len && (char.IsLetter(text[i + 1]) || text[i + 1] == '-'))
            {
                int start = i;
                while (i < len && (char.IsLetterOrDigit(text[i]) || text[i] == '-' || text[i] == '_')) i++;
                AddRun(p, text[start..i], AttributeBrush);
                continue;
            }

            // Environment variables $VAR or ${VAR}
            if (c == '$' && i + 1 < len)
            {
                int start = i++;
                if (i < len && text[i] == '{')
                {
                    while (i < len && text[i] != '}' && text[i] != '\n') i++;
                    if (i < len && text[i] == '}') i++;
                }
                else
                {
                    while (i < len && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;
                }
                AddRun(p, text[start..i], VariableBrush, isBold: true);
                continue;
            }

            // Word / command
            if (char.IsLetter(c) || c == '_')
            {
                int start = i;
                while (i < len && (char.IsLetterOrDigit(text[i]) || text[i] == '_' || text[i] == '.')) i++;
                string word = text[start..i];
                if (ShellCommands.Contains(word))
                    AddRun(p, word, KeywordBrush, isBold: true);
                else
                    AddRun(p, word, DefaultBrush);
                continue;
            }

            // Operators | && ; > <
            if ("|&;><".Contains(c))
            {
                AddRun(p, c.ToString(), PunctuationBrush, isBold: true);
                i++;
                continue;
            }

            AddRun(p, c.ToString(), DefaultBrush);
            i++;
        }
    }

    private static void HighlightGeneralCode(Paragraph p, string text)
    {
        int i = 0;
        int len = text.Length;

        while (i < len)
        {
            char c = text[i];
            if (c == '\r') { i++; continue; }
            if (c == '\n') { AddLineBreak(p); i++; continue; }

            // Single line comment // ... or # ...
            if ((c == '/' && i + 1 < len && text[i + 1] == '/') || (c == '#' && (i == 0 || text[i - 1] == '\n' || char.IsWhiteSpace(text[i - 1]))))
            {
                int start = i;
                while (i < len && text[i] != '\n' && text[i] != '\r') i++;
                AddRun(p, text[start..i], CommentBrush, isItalic: true);
                continue;
            }

            // Multi line comment /* ... */
            if (c == '/' && i + 1 < len && text[i + 1] == '*')
            {
                int start = i;
                i += 2;
                while (i < len && !(text[i - 1] == '*' && text[i] == '/'))
                {
                    if (text[i] == '\n') { AddRun(p, text[start..i], CommentBrush, isItalic: true); AddLineBreak(p); start = i + 1; }
                    i++;
                }
                if (i < len) i++;
                if (start < i) AddRun(p, text[start..i], CommentBrush, isItalic: true);
                continue;
            }

            // Strings '...' or "..." or `...`
            if (c == '"' || c == '\'' || c == '`')
            {
                char quote = c;
                int start = i++;
                while (i < len && text[i] != quote && (quote == '`' || text[i] != '\n'))
                {
                    if (text[i] == '\\' && i + 1 < len) i++;
                    if (text[i] == '\n') { AddRun(p, text[start..i], StringBrush); AddLineBreak(p); start = i + 1; }
                    i++;
                }
                if (i < len && text[i] == quote) i++;
                if (start < i) AddRun(p, text[start..i], StringBrush);
                continue;
            }

            // Numbers
            if (char.IsDigit(c))
            {
                int start = i;
                while (i < len && (char.IsDigit(text[i]) || text[i] == '.' || text[i] == 'x' || text[i] == 'X' || text[i] == 'f' || text[i] == 'L')) i++;
                AddRun(p, text[start..i], NumberBrush);
                continue;
            }

            // Words / Identifiers / Keywords
            if (char.IsLetter(c) || c == '_')
            {
                int start = i;
                while (i < len && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;
                string word = text[start..i];
                if (CodeKeywords.Contains(word))
                    AddRun(p, word, KeywordBrush, isBold: true);
                else
                    AddRun(p, word, DefaultBrush);
                continue;
            }

            // Operators & Punctuation
            if ("{}[]();,.<>=!+-*/%&|^?:~".Contains(c))
            {
                AddRun(p, c.ToString(), PunctuationBrush);
                i++;
                continue;
            }

            AddRun(p, c.ToString(), DefaultBrush);
            i++;
        }
    }
}
