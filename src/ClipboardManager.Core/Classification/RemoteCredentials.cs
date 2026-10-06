using System;
using System.Text.RegularExpressions;

namespace ClipboardManager.Core.Classification;

/// <summary>
/// Parsed credentials for remote desktop software (UltraViewer, TeamViewer, AnyDesk).
/// </summary>
public sealed record RemoteCredentials(
    string Provider,
    string Id,
    string NormalizedId,
    string Password)
{
    private const RegexOptions Opts = RegexOptions.Compiled | RegexOptions.CultureInvariant;

    // OCR or Vietnamese/English labels for ID: "ID", "IO", "I0", "Your ID", "ID của bạn", "ID đối tác", "Partner ID"...
    private static readonly Regex OcrFullRx = new(
        @"(?:(?:IO|ID|I0|Partner\s*ID|Your\s*ID|ID\s*của\s*bạn|ID\s*đối\s*tác)[^\d\n\r:]*[:\s]*)?(?<id>(?:\d{1,4}(?:[\s.-]\d{3}){1,3}|\d{3,10}))[\s\S]*?(?:(?:Pass(?:word)?|M[a-zà-ỹA-ZÀ-Ỹ]*t\s*kh\w*|MK|Pwd)[^\w\n\r:]*[:\s]+)(?<pass>[A-Za-z0-9@#$%^&*_]{3,12})",
        Opts | RegexOptions.IgnoreCase);

    // Single or multi-line plain text: e.g. "39 850 251 - 4461" or "39 850 251\n4461" or "ID: 39 850 251 Pass: 4461"
    private static readonly Regex SimpleRx = new(
        @"^\s*(?:(?:ID|Ultra(?:viewer)?|TeamViewer|AnyDesk)[^\d\n:]*[:\s]*)?(?<id>(?:\d{1,4}(?:[\s.-]\d{3}){1,3}|\d{3,10}))\s*(?:[-–—\/|,;\n\r]+|\s+)\s*(?:(?:Pass(?:word)?|M[a-zà-ỹA-ZÀ-Ỹ]*t\s*kh\w*|MK|Pwd)[^\w\n:]*[:\s]*)?(?<pass>[A-Za-z0-9@#$%^&*_]{3,12})\s*$",
        Opts | RegexOptions.IgnoreCase);

    // Labeled anywhere inside text: "ID: 39 850 251 ... Pass: 4461"
    private static readonly Regex LabeledRx = new(
        @"(?:(?:ID|IO|I0|Partner\s*ID|Your\s*ID|ID\s*của\s*bạn|ID\s*đối\s*tác)[^\d\n\r:]*[:\s]+)(?<id>(?:\d{1,4}(?:[\s.-]\d{3}){1,3}|\d{3,10}))[^\w\n\r]*(?:(?:\r?\n|[,\s\/-]|và)+)?(?:(?:Pass(?:word)?|M[a-zà-ỹA-ZÀ-Ỹ]*t\s*kh\w*|MK|Pwd)[^\w\n\r:]*[:\s]+)(?<pass>[A-Za-z0-9@#$%^&*_]{3,12})",
        Opts | RegexOptions.IgnoreCase);

    /// <summary>
    /// Attempts to parse remote credentials from raw text (chat message, notes, or OCR text).
    /// </summary>
    public static bool TryParse(string? text, out RemoteCredentials? credentials)
    {
        credentials = null;
        if (string.IsNullOrWhiteSpace(text) || text.Length > 5000) return false;

        var t = text.Trim();

        bool isTeam = Regex.IsMatch(t, @"\bteam(?:viewer)?\b", RegexOptions.IgnoreCase);
        bool isAnyDesk = Regex.IsMatch(t, @"\banydesk\b", RegexOptions.IgnoreCase);
        bool isUltra = Regex.IsMatch(t, @"\bultra(?:viewer)?\b", RegexOptions.IgnoreCase);

        // 1. Try simple 1-line or 2-line format
        var m = SimpleRx.Match(t);
        if (m.Success)
        {
            var rawId = m.Groups["id"].Value.Trim();
            var normId = Regex.Replace(rawId, @"\D", "");
            var pass = m.Groups["pass"].Value.Trim();

            // Validate plausible length: ID usually 3-10 digits, pass 3-12 chars
            if (normId.Length >= 3 && pass.Length >= 3 && (!int.TryParse(normId, out _) || normId != pass))
            {
                string provider = InferProvider(isUltra, isTeam, isAnyDesk, normId);
                credentials = new RemoteCredentials(provider, rawId, normId, pass);
                return true;
            }
        }

        // 2. Try OCR text format (may have multiple lines with intermediate noise)
        m = OcrFullRx.Match(t);
        if (m.Success)
        {
            var rawId = m.Groups["id"].Value.Trim();
            var normId = Regex.Replace(rawId, @"\D", "");
            var pass = m.Groups["pass"].Value.Trim();

            if (normId.Length >= 3 && pass.Length >= 3)
            {
                string provider = InferProvider(isUltra, isTeam, isAnyDesk, normId);
                credentials = new RemoteCredentials(provider, rawId, normId, pass);
                return true;
            }
        }

        // 3. Try labeled format
        m = LabeledRx.Match(t);
        if (m.Success)
        {
            var rawId = m.Groups["id"].Value.Trim();
            var normId = Regex.Replace(rawId, @"\D", "");
            var pass = m.Groups["pass"].Value.Trim();

            if (normId.Length >= 3 && pass.Length >= 3)
            {
                string provider = InferProvider(isUltra, isTeam, isAnyDesk, normId);
                credentials = new RemoteCredentials(provider, rawId, normId, pass);
                return true;
            }
        }

        return false;
    }

    private static string InferProvider(bool isUltra, bool isTeam, bool isAnyDesk, string normId)
    {
        if (isTeam) return "TeamViewer";
        if (isAnyDesk) return "AnyDesk";
        if (isUltra) return "UltraViewer";

        // UltraViewer typically uses 8 digits (e.g. 39 850 251)
        if (normId.Length == 8) return "UltraViewer";

        // TeamViewer typically uses 9-10 digits
        if (normId.Length is 9 or 10) return "TeamViewer";

        return "UltraViewer";
    }

    /// <summary>
    /// Friendly summary for UI titles: "UltraViewer: 39 850 251 | Pass: 4461".
    /// </summary>
    public string ToFormattedTitle() => $"{Provider}: {Id} | Pass: {Password}";

    /// <summary>
    /// ID and Password separated by tab, ideal for field navigation.
    /// </summary>
    public string ToTabSeparated() => $"{Id}\t{Password}";
}
