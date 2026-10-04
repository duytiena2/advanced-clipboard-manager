using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using ClipboardManager.Core.Models;

namespace ClipboardManager.Mac.UI;

internal static class MacSyntaxHighlighter
{
    private static readonly IBrush KeywordBrush = new SolidColorBrush(Color.Parse("#0066CC"));
    private static readonly IBrush StringBrush = new SolidColorBrush(Color.Parse("#A31515"));
    private static readonly IBrush NumberBrush = new SolidColorBrush(Color.Parse("#098658"));
    private static readonly IBrush CommentBrush = new SolidColorBrush(Color.Parse("#6E7781"));
    private static readonly IBrush KeyBrush = new SolidColorBrush(Color.Parse("#795E26"));
    private static readonly IBrush DefaultBrush = new SolidColorBrush(Color.Parse("#24292F"));

    private static readonly HashSet<string> SqlKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT", "FROM", "WHERE", "INSERT", "INTO", "UPDATE", "SET", "DELETE",
        "JOIN", "INNER", "LEFT", "RIGHT", "OUTER", "CROSS", "FULL", "ON",
        "GROUP", "BY", "ORDER", "HAVING", "LIMIT", "OFFSET", "UNION", "ALL",
        "AS", "AND", "OR", "NOT", "IN", "IS", "NULL", "LIKE", "BETWEEN",
        "EXISTS", "CASE", "WHEN", "THEN", "ELSE", "END", "CREATE", "TABLE",
        "INDEX", "VIEW", "TRIGGER", "DATABASE", "DROP", "ALTER", "ADD",
        "PRIMARY", "KEY", "FOREIGN", "REFERENCES", "DEFAULT", "CHECK", "WITH"
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
        "function", "let", "var", "def", "import", "package", "type", "func", "nil"
    };

    private static readonly Regex TokenRegex = new(
        @"(?<comment>--[^\r\n]*|//[^\r\n]*|/\*[\s\S]*?\*/)|" +
        @"(?<string>""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\])*')|" +
        @"(?<number>\b\d+(?:\.\d+)?\b)|" +
        @"(?<word>\b[A-Za-z_][A-Za-z0-9_]*\b)|" +
        @"(?<ws>\s+)|" +
        @"(?<other>[^\w\s])",
        RegexOptions.Compiled);

    public static void Highlight(SelectableTextBlock block, ClipboardItem item)
    {
        block.Inlines?.Clear();
        if (string.IsNullOrEmpty(item.TextContent)) return;

        var text = item.TextContent;
        // Limit highlight length for performance
        if (text.Length > 20000)
        {
            block.Inlines?.Add(new Run(text));
            return;
        }

        bool isSql = item.Subtype.Equals("sql", StringComparison.OrdinalIgnoreCase);
        bool isJson = item.Subtype.Equals("json", StringComparison.OrdinalIgnoreCase);
        bool isCode = item.Kind == ContentKind.Code || isSql || isJson;

        if (!isCode)
        {
            block.Inlines?.Add(new Run(text));
            return;
        }

        var inlines = new InlineCollection();
        var matches = TokenRegex.Matches(text);

        foreach (Match m in matches)
        {
            if (m.Groups["comment"].Success)
            {
                inlines.Add(new Run(m.Value) { Foreground = CommentBrush, FontStyle = FontStyle.Italic });
            }
            else if (m.Groups["string"].Success)
            {
                // In JSON, keys are strings followed by colon
                bool isKey = isJson && m.Index + m.Length < text.Length && text.AsSpan(m.Index + m.Length).TrimStart().StartsWith(":");
                inlines.Add(new Run(m.Value) { Foreground = isKey ? KeyBrush : StringBrush });
            }
            else if (m.Groups["number"].Success)
            {
                inlines.Add(new Run(m.Value) { Foreground = NumberBrush });
            }
            else if (m.Groups["word"].Success)
            {
                string word = m.Value;
                bool isKeyword = (isSql && SqlKeywords.Contains(word)) || CodeKeywords.Contains(word);
                if (isKeyword)
                {
                    inlines.Add(new Run(word) { Foreground = KeywordBrush, FontWeight = FontWeight.Bold });
                }
                else
                {
                    inlines.Add(new Run(word) { Foreground = DefaultBrush });
                }
            }
            else
            {
                inlines.Add(new Run(m.Value));
            }
        }

        block.Inlines = inlines;
    }
}
