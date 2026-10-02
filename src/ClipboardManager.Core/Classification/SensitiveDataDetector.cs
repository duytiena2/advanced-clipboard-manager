using System.Text.RegularExpressions;

namespace ClipboardManager.Core.Classification;

/// <summary>
/// Detects secrets (API keys, tokens, private keys, credentials) using local regex rules only.
/// Nothing is ever sent off the machine.
/// </summary>
public static class SensitiveDataDetector
{
    private const RegexOptions Opts = RegexOptions.Compiled | RegexOptions.CultureInvariant;

    // (subtype, pattern) — ordered from most to least specific.
    private static readonly (string Subtype, Regex Pattern)[] Rules =
    {
        ("private-key", new Regex(@"-----BEGIN (?:RSA |EC |DSA |OPENSSH |ENCRYPTED |PGP )?PRIVATE KEY(?: BLOCK)?-----", Opts)),
        ("jwt", new Regex(@"^eyJ[A-Za-z0-9_-]{8,}\.eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}$", Opts)),
        ("aws-key", new Regex(@"\b(?:AKIA|ASIA)[0-9A-Z]{16}\b", Opts)),
        ("api-key", new Regex(@"\b(?:sk|pk|rk)_(?:live|test)_[A-Za-z0-9]{16,}\b", Opts)),           // Stripe-style
        ("api-key", new Regex(@"\bsk-(?:proj-|ant-)?[A-Za-z0-9_-]{20,}\b", Opts)),                    // OpenAI/Anthropic-style
        ("token", new Regex(@"\bgh[pousr]_[A-Za-z0-9]{36,}\b", Opts)),                                // GitHub
        ("token", new Regex(@"\bxox[abprs]-[A-Za-z0-9-]{10,}\b", Opts)),                               // Slack
        ("token", new Regex(@"\bAIza[0-9A-Za-z_-]{35}\b", Opts)),                                     // Google API key
        ("auth-header", new Regex(@"^\s*(?:Authorization:\s*)?(?:Bearer|Basic)\s+[A-Za-z0-9._~+/=-]{16,}\s*$", Opts | RegexOptions.IgnoreCase)),
        ("password", new Regex(@"^\s*(?:password|passwd|pwd|secret|api[_-]?key|token)\s*[:=]\s*\S+\s*$", Opts | RegexOptions.IgnoreCase)),
        ("connection-string", new Regex(@"(?:^|;)\s*(?:Password|Pwd)\s*=\s*[^;]+", Opts | RegexOptions.IgnoreCase)),
        ("connection-string", new Regex(@"^[a-z][a-z0-9+.-]*://[^\s:/@]+:[^\s@/]+@[^\s]+$", Opts | RegexOptions.IgnoreCase)), // scheme://user:pass@host
    };

    /// <summary>Returns the secret subtype, or null when the text does not look sensitive.</summary>
    public static string? Detect(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 20_000) return null;
        var trimmed = text.Trim();
        foreach (var (subtype, pattern) in Rules)
        {
            if (pattern.IsMatch(trimmed)) return subtype;
        }
        return LooksLikeRandomSecret(trimmed) ? "token" : null;
    }

    /// <summary>
    /// Single "word" of 24+ chars mixing upper, lower and digits with high entropy — typical for generated keys.
    /// </summary>
    internal static bool LooksLikeRandomSecret(string s)
    {
        if (s.Length < 24 || s.Length > 512) return false;
        foreach (var c in s)
        {
            if (char.IsWhiteSpace(c)) return false;
            if (!(char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '+' or '/' or '=')) return false;
        }
        bool upper = false, lower = false, digit = false;
        foreach (var c in s)
        {
            upper |= char.IsAsciiLetterUpper(c);
            lower |= char.IsAsciiLetterLower(c);
            digit |= char.IsAsciiDigit(c);
        }
        if (!(upper && lower && digit)) return false;
        return ShannonEntropy(s) >= 4.0;
    }

    internal static double ShannonEntropy(string s)
    {
        var counts = new Dictionary<char, int>();
        foreach (var c in s) counts[c] = counts.TryGetValue(c, out var n) ? n + 1 : 1;
        double entropy = 0;
        foreach (var n in counts.Values)
        {
            double p = (double)n / s.Length;
            entropy -= p * Math.Log2(p);
        }
        return entropy;
    }
}
