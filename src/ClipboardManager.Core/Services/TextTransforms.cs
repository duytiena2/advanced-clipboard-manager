using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClipboardManager.Core.Classification;

namespace ClipboardManager.Core.Services;

/// <summary>A named text conversion offered in Quick Paste (Ctrl+K).</summary>
public sealed record TextTransform(string Id, string Name, Func<string, string> Apply);

/// <summary>Thrown when the input is not valid for a transform (e.g. "Format JSON" on non-JSON).</summary>
public sealed class TransformException : Exception
{
    public TransformException(string message) : base(message) { }
}

/// <summary>Local text conversions: case, whitespace, JSON, SQL, Base64, URL.</summary>
public static class TextTransforms
{
    public static IReadOnlyList<TextTransform> All { get; } = new TextTransform[]
    {
        new("upper", "UPPERCASE", s => s.ToUpper(CultureInfo.CurrentCulture)),
        new("lower", "lowercase", s => s.ToLower(CultureInfo.CurrentCulture)),
        new("title", "Title Case", TitleCase),
        new("sentence", "Sentence case", SentenceCase),
        new("trim", "Trim extra whitespace", CollapseWhitespace),
        new("one-line", "Join into one line", s => Regex.Replace(s.Trim(), @"\s*\r?\n\s*", " ")),
        new("remove-blank-lines", "Remove blank lines", RemoveBlankLines),
        new("json-pretty", "Format JSON", s => FormatJson(s, indented: true)),
        new("json-minify", "Minify JSON", s => FormatJson(s, indented: false)),
        new("sql", "Format SQL", SqlFormatter.Format),
        new("base64-encode", "Base64 encode", s => Convert.ToBase64String(Encoding.UTF8.GetBytes(s))),
        new("base64-decode", "Base64 decode", Base64Decode),
        new("url-encode", "URL encode", Uri.EscapeDataString),
        new("url-decode", "URL decode", s => WebUtility.UrlDecode(s)),
        new("remote-id", "Extract Remote ID", ExtractRemoteId),
        new("remote-pass", "Extract Remote Password", ExtractRemotePass),
        new("remote-tab", "Remote Auto-Tab (ID + Tab + Pass)", ExtractRemoteTab),
    };

    public static TextTransform Get(string id) =>
        All.FirstOrDefault(t => t.Id == id) ?? throw new ArgumentException("Unknown transform " + id, nameof(id));

    public static string Apply(string id, string text) => Get(id).Apply(text);

    private static string TitleCase(string s) =>
        CultureInfo.CurrentCulture.TextInfo.ToTitleCase(s.ToLower(CultureInfo.CurrentCulture));

    private static string SentenceCase(string s)
    {
        var lower = s.ToLower(CultureInfo.CurrentCulture);
        var sb = new StringBuilder(lower.Length);
        bool capitalize = true;
        foreach (var c in lower)
        {
            if (capitalize && char.IsLetter(c))
            {
                sb.Append(char.ToUpper(c, CultureInfo.CurrentCulture));
                capitalize = false;
                continue;
            }
            sb.Append(c);
            if (c is '.' or '!' or '?' or '\n') capitalize = true;
        }
        return sb.ToString();
    }

    /// <summary>Trims every line, collapses runs of spaces/tabs, and drops leading/trailing blank lines.</summary>
    private static string CollapseWhitespace(string s)
    {
        var lines = s.Replace("\r\n", "\n").Split('\n').Select(l => Regex.Replace(l.Trim(), @"[ \t ]+", " "));
        return string.Join(Environment.NewLine, lines).Trim();
    }

    private static string RemoveBlankLines(string s) =>
        string.Join(Environment.NewLine, s.Replace("\r\n", "\n").Split('\n').Where(l => l.Trim().Length > 0));

    private static readonly JsonWriterOptions Pretty = new() { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly JsonWriterOptions Compact = new() { Indented = false, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string FormatSql(string sql) => SqlFormatter.Format(sql);

    public static string FormatJson(string s, bool indented = true)
    {
        try
        {
            using var doc = JsonDocument.Parse(s, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            using var ms = new MemoryStream();
            using (var w = new Utf8JsonWriter(ms, indented ? Pretty : Compact)) doc.WriteTo(w);
            return Encoding.UTF8.GetString(ms.ToArray());
        }
        catch (JsonException ex)
        {
            throw new TransformException("Not valid JSON: " + ex.Message);
        }
    }

    private static string Base64Decode(string s)
    {
        var t = Regex.Replace(s, @"\s+", "").Replace('-', '+').Replace('_', '/'); // also accept base64url
        t = t.PadRight(t.Length + (4 - t.Length % 4) % 4, '=');
        byte[] bytes;
        try { bytes = Convert.FromBase64String(t); }
        catch (FormatException) { throw new TransformException("Not valid Base64"); }
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw new TransformException("Decoded data is binary, not text");
        }
    }

    private static string ExtractRemoteId(string s) =>
        RemoteCredentials.TryParse(s, out var creds) && creds is not null ? creds.Id : throw new TransformException("No remote desktop credentials found");

    private static string ExtractRemotePass(string s) =>
        RemoteCredentials.TryParse(s, out var creds) && creds is not null ? creds.Password : throw new TransformException("No remote desktop credentials found");

    private static string ExtractRemoteTab(string s) =>
        RemoteCredentials.TryParse(s, out var creds) && creds is not null ? creds.ToTabSeparated() : throw new TransformException("No remote desktop credentials found");
}

/// <summary>
/// Small, dependency-free SQL pretty-printer: one clause per line, AND/OR and JOIN conditions indented,
/// keywords upper-cased. Strings, quoted identifiers and comments are left untouched.
/// </summary>
public static class SqlFormatter
{
    private static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "select", "from", "where", "and", "or", "not", "in", "is", "null", "like", "between", "exists", "as", "on", "join",
        "inner", "left", "right", "full", "outer", "cross", "group", "by", "order", "having", "limit", "offset", "union", "all",
        "distinct", "insert", "into", "values", "update", "set", "delete", "create", "table", "alter", "drop", "index", "view",
        "with", "case", "when", "then", "else", "end", "asc", "desc", "returning", "top", "count", "sum", "avg", "min", "max",
        "primary", "key", "foreign", "references", "default", "unique", "using", "except", "intersect", "fetch", "next", "rows", "only",
    };

    // Clause keywords (possibly multi-word) that start a new line at the current nesting level.
    private static readonly string[][] Clauses =
    {
        new[] { "select" }, new[] { "from" }, new[] { "where" }, new[] { "group", "by" }, new[] { "order", "by" },
        new[] { "having" }, new[] { "limit" }, new[] { "offset" }, new[] { "union", "all" }, new[] { "union" },
        new[] { "except" }, new[] { "intersect" }, new[] { "values" }, new[] { "set" }, new[] { "returning" },
        new[] { "insert", "into" }, new[] { "delete", "from" }, new[] { "update" },
        new[] { "left", "outer", "join" }, new[] { "right", "outer", "join" }, new[] { "full", "outer", "join" },
        new[] { "inner", "join" }, new[] { "left", "join" }, new[] { "right", "join" }, new[] { "full", "join" },
        new[] { "cross", "join" }, new[] { "join" },
    };

    private enum T { Word, Symbol, Literal, Comment, Open, Close, Comma }

    public static string Format(string sql)
    {
        var tokens = Tokenize(sql).ToList();
        if (tokens.Count == 0) return sql;

        var sb = new StringBuilder();
        int depth = 0;
        int caseDepth = 0;
        var parens = new Stack<(bool Sub, int SavedDepth, int LineLevel)>();
        string Indent(int extra = 0) => new(' ', 4 * Math.Max(0, depth + extra));

        void NewLine(int extra = 0)
        {
            TrimEndSpaces(sb);
            if (sb.Length > 0) sb.Append(Environment.NewLine);
            sb.Append(Indent(extra));
        }

        for (int i = 0; i < tokens.Count; i++)
        {
            var (kind, text) = tokens[i];
            if (kind == T.Word && Keywords.Contains(text)) text = text.ToUpperInvariant();

            int clauseLen = kind == T.Word ? MatchClause(tokens, i) : 0;
            if (clauseLen > 0 && caseDepth == 0)
            {
                if (sb.Length > 0) NewLine();
                for (int k = 0; k < clauseLen; k++)
                {
                    if (k > 0) sb.Append(' ');
                    sb.Append(tokens[i + k].Text.ToUpperInvariant());
                }
                i += clauseLen - 1;
                sb.Append(' ');
                continue;
            }

            switch (kind)
            {
                case T.Word when text is "AND" or "OR" && caseDepth == 0 && !PrecededByBetween(tokens, i):
                    NewLine(1);
                    sb.Append(text).Append(' ');
                    break;
                case T.Word when text == "ON" && caseDepth == 0:
                    NewLine(1);
                    sb.Append(text).Append(' ');
                    break;
                case T.Word when text == "CASE":
                    caseDepth++;
                    sb.Append(text).Append(' ');
                    break;
                case T.Word when text == "END" && caseDepth > 0:
                    caseDepth--;
                    sb.Append(text).Append(' ');
                    break;
                case T.Open:
                    bool sub = i + 1 < tokens.Count && tokens[i + 1].Kind == T.Word &&
                               (tokens[i + 1].Text.Equals("select", StringComparison.OrdinalIgnoreCase) ||
                                tokens[i + 1].Text.Equals("with", StringComparison.OrdinalIgnoreCase));
                    if (sub)
                    {
                        // The subquery is indented one level deeper than the line that opens it; ")" returns to that line's level.
                        int lineLevel = CurrentLineIndent(sb) / 4;
                        parens.Push((true, depth, lineLevel));
                        sb.Append('(');
                        depth = lineLevel + 1;
                    }
                    else
                    {
                        // count(x), my_func(x) hug the parenthesis; IN (…), VALUES (…), = (…) keep a space.
                        bool call = i > 0 && tokens[i - 1].Kind is T.Word or T.Literal && !IsSpacedKeyword(tokens[i - 1].Text);
                        if (call) TrimEndSpaces(sb);
                        parens.Push((false, depth, 0));
                        sb.Append('(');
                    }
                    break;
                case T.Close:
                    if (parens.Count > 0 && parens.Peek().Sub)
                    {
                        var (_, saved, lineLevel) = parens.Pop();
                        depth = lineLevel;
                        NewLine();
                        depth = saved;
                    }
                    else
                    {
                        if (parens.Count > 0) parens.Pop();
                        TrimEndSpaces(sb);
                    }
                    sb.Append(") ");
                    break;
                case T.Comma:
                    TrimEndSpaces(sb);
                    sb.Append(',');
                    bool listContext = parens.Count == 0 || parens.Peek().Sub;
                    if (listContext && caseDepth == 0 && InSelectList(tokens, i)) NewLine(1);
                    else sb.Append(' ');
                    break;
                case T.Comment when text.StartsWith("--"):
                    sb.Append(text);
                    NewLine();
                    break;
                case T.Symbol when text == ";":
                    TrimEndSpaces(sb);
                    sb.Append(';');
                    depth = 0;
                    parens.Clear();
                    if (i < tokens.Count - 1) sb.Append(Environment.NewLine);
                    break;
                case T.Symbol when text == ".":
                    TrimEndSpaces(sb);
                    sb.Append('.');
                    break;
                default:
                    sb.Append(text).Append(' ');
                    break;
            }
        }
        TrimEndSpaces(sb);
        return sb.ToString().Trim();
    }

    private static int MatchClause(List<(T Kind, string Text)> tokens, int i)
    {
        foreach (var clause in Clauses)
        {
            if (i + clause.Length > tokens.Count) continue;
            bool ok = true;
            for (int k = 0; k < clause.Length && ok; k++)
                ok = tokens[i + k].Kind == T.Word && tokens[i + k].Text.Equals(clause[k], StringComparison.OrdinalIgnoreCase);
            if (ok) return clause.Length;
        }
        return 0;
    }

    /// <summary>Commas directly inside SELECT (column list) or SET/VALUES break lines; commas inside function calls don't.</summary>
    private static bool InSelectList(List<(T Kind, string Text)> tokens, int i)
    {
        int parens = 0;
        for (int j = i - 1; j >= 0; j--)
        {
            var (kind, text) = tokens[j];
            if (kind == T.Close) parens++;
            else if (kind == T.Open) { if (parens == 0) return false; parens--; }
            else if (parens == 0 && kind == T.Word)
            {
                if (text.Equals("select", StringComparison.OrdinalIgnoreCase) || text.Equals("set", StringComparison.OrdinalIgnoreCase)) return true;
                if (MatchClause(tokens, j) > 0) return false;
            }
        }
        return false;
    }

    private static bool PrecededByBetween(List<(T Kind, string Text)> tokens, int i)
    {
        // "x BETWEEN 1 AND 5": the AND belongs to BETWEEN.
        if (!tokens[i].Text.Equals("and", StringComparison.OrdinalIgnoreCase)) return false;
        return i >= 2 && tokens[i - 2].Text.Equals("between", StringComparison.OrdinalIgnoreCase);
    }

    private static readonly HashSet<string> FunctionKeywords = new(StringComparer.OrdinalIgnoreCase) { "count", "sum", "avg", "min", "max" };

    /// <summary>Keywords followed by a spaced "(": IN (…), VALUES (…), AS (…), EXISTS (…).</summary>
    private static bool IsSpacedKeyword(string word) => Keywords.Contains(word) && !FunctionKeywords.Contains(word);

    private static int CurrentLineIndent(StringBuilder sb)
    {
        int start = sb.Length;
        while (start > 0 && sb[start - 1] != '\n') start--;
        int n = 0;
        while (start + n < sb.Length && sb[start + n] == ' ') n++;
        return n;
    }

    private static void TrimEndSpaces(StringBuilder sb)
    {
        while (sb.Length > 0 && sb[^1] == ' ') sb.Length--;
    }

    private static IEnumerable<(T Kind, string Text)> Tokenize(string s)
    {
        int i = 0;
        while (i < s.Length)
        {
            char c = s[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }

            if (c == '-' && i + 1 < s.Length && s[i + 1] == '-')
            {
                int end = s.IndexOf('\n', i);
                if (end < 0) end = s.Length;
                yield return (T.Comment, s[i..end].TrimEnd('\r'));
                i = end;
                continue;
            }
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
            {
                int end = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                end = end < 0 ? s.Length : end + 2;
                yield return (T.Comment, s[i..end]);
                i = end;
                continue;
            }
            if (c is '\'' or '"' or '`' or '[')
            {
                char close = c == '[' ? ']' : c;
                int j = i + 1;
                while (j < s.Length)
                {
                    if (s[j] == close)
                    {
                        if (close != ']' && j + 1 < s.Length && s[j + 1] == close) { j += 2; continue; } // '' escape
                        break;
                    }
                    j++;
                }
                j = Math.Min(j + 1, s.Length);
                yield return (T.Literal, s[i..j]);
                i = j;
                continue;
            }
            if (char.IsLetterOrDigit(c) || c is '_' or '@' or '#' or '$' or ':')
            {
                int j = i;
                while (j < s.Length && (char.IsLetterOrDigit(s[j]) || s[j] is '_' or '@' or '#' or '$' or ':' || (s[j] == '.' && j + 1 < s.Length && char.IsDigit(s[j + 1]) && char.IsDigit(s[i]))))
                    j++;
                yield return (T.Word, s[i..j]);
                i = j;
                continue;
            }
            if (c == '(') { yield return (T.Open, "("); i++; continue; }
            if (c == ')') { yield return (T.Close, ")"); i++; continue; }
            if (c == ',') { yield return (T.Comma, ","); i++; continue; }

            // Operators: keep multi-char ones together.
            string[] ops = { "<>", "<=", ">=", "!=", "||", "::", "->>", "->" };
            var op = ops.FirstOrDefault(o => string.CompareOrdinal(s, i, o, 0, o.Length) == 0);
            if (op is not null) { yield return (T.Symbol, op); i += op.Length; continue; }
            yield return (T.Symbol, c.ToString());
            i++;
        }
    }
}
