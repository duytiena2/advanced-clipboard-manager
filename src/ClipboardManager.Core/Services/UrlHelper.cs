using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ClipboardManager.Core.Services;

public record UrlParameter(string Key, string Value, bool IsTracking);

public record UrlAnalysis(
    string OriginalUrl,
    bool IsValid,
    string Scheme,
    string Host,
    string Path,
    IReadOnlyList<UrlParameter> Parameters,
    string CleanUrl,
    bool HasTrackingParameters
);

/// <summary>
/// Helper for inspecting, parsing, and cleaning URLs and query parameters.
/// </summary>
public static class UrlHelper
{
    private static readonly HashSet<string> KnownTrackingParams = new(StringComparer.OrdinalIgnoreCase)
    {
        "fbclid",
        "gclid",
        "gbraid",
        "wbraid",
        "msclkid",
        "mc_cid",
        "mc_eid",
        "igshid",
        "si",
        "spm",
        "scm",
        "_hsenc",
        "_hsmi",
        "yclid",
        "mkt_tok",
        "trk",
        "trackingid",
        "vgo_ee"
    };

    public static bool IsTrackingParam(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        var trimmed = key.Trim();
        if (trimmed.StartsWith("utm_", StringComparison.OrdinalIgnoreCase)) return true;
        if (trimmed.StartsWith("ref_", StringComparison.OrdinalIgnoreCase)) return true;
        return KnownTrackingParams.Contains(trimmed);
    }

    public static IReadOnlyList<UrlParameter> ParseQueryParameters(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return Array.Empty<UrlParameter>();

        int queryIndex = url.IndexOf('?');
        if (queryIndex < 0 || queryIndex == url.Length - 1) return Array.Empty<UrlParameter>();

        int fragmentIndex = url.IndexOf('#', queryIndex);
        string queryString = fragmentIndex >= 0
            ? url.Substring(queryIndex + 1, fragmentIndex - queryIndex - 1)
            : url.Substring(queryIndex + 1);

        if (string.IsNullOrWhiteSpace(queryString)) return Array.Empty<UrlParameter>();

        var pairs = queryString.Split('&', StringSplitOptions.RemoveEmptyEntries);
        var result = new List<UrlParameter>(pairs.Length);

        foreach (var pair in pairs)
        {
            int eqIndex = pair.IndexOf('=');
            string rawKey = eqIndex >= 0 ? pair[..eqIndex] : pair;
            string rawVal = eqIndex >= 0 ? pair[(eqIndex + 1)..] : "";

            string key = SafeUrlDecode(rawKey);
            string val = SafeUrlDecode(rawVal);
            bool isTracking = IsTrackingParam(key);

            result.Add(new UrlParameter(key, val, isTracking));
        }

        return result;
    }

    public static string GetCleanUrl(string url, bool stripAllQueryParams = false)
    {
        if (string.IsNullOrWhiteSpace(url)) return url ?? "";

        var cand = url.Trim();
        int queryIndex = cand.IndexOf('?');
        if (queryIndex < 0) return cand;

        int fragmentIndex = cand.IndexOf('#', queryIndex);
        string baseUrl = cand[..queryIndex];
        string fragment = fragmentIndex >= 0 ? cand[fragmentIndex..] : "";

        if (stripAllQueryParams)
        {
            return baseUrl + fragment;
        }

        var allParams = ParseQueryParameters(cand);
        var keptParams = allParams.Where(p => !p.IsTracking).ToList();

        if (keptParams.Count == 0)
        {
            return baseUrl + fragment;
        }

        var sb = new StringBuilder(baseUrl).Append('?');
        for (int i = 0; i < keptParams.Count; i++)
        {
            if (i > 0) sb.Append('&');
            sb.Append(Uri.EscapeDataString(keptParams[i].Key));
            if (!string.IsNullOrEmpty(keptParams[i].Value))
            {
                sb.Append('=').Append(Uri.EscapeDataString(keptParams[i].Value));
            }
        }

        sb.Append(fragment);
        return sb.ToString();
    }

    public static UrlAnalysis AnalyzeUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return new UrlAnalysis("", false, "—", "", "", Array.Empty<UrlParameter>(), "", false);
        }

        var raw = url.Trim();
        var cand = raw.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? "https://" + raw : raw;

        if (Uri.TryCreate(cand, UriKind.Absolute, out var uri))
        {
            var parameters = ParseQueryParameters(raw);
            bool hasTracking = parameters.Any(p => p.IsTracking);
            string cleanUrl = GetCleanUrl(raw, stripAllQueryParams: false);

            return new UrlAnalysis(
                OriginalUrl: raw,
                IsValid: true,
                Scheme: uri.Scheme,
                Host: uri.Host,
                Path: string.IsNullOrEmpty(uri.AbsolutePath) ? "/" : uri.AbsolutePath,
                Parameters: parameters,
                CleanUrl: cleanUrl,
                HasTrackingParameters: hasTracking
            );
        }

        // Fallback when URI cannot be parsed as absolute
        var fallbackParams = ParseQueryParameters(raw);
        return new UrlAnalysis(
            OriginalUrl: raw,
            IsValid: false,
            Scheme: "—",
            Host: "",
            Path: raw,
            Parameters: fallbackParams,
            CleanUrl: GetCleanUrl(raw, stripAllQueryParams: false),
            HasTrackingParameters: fallbackParams.Any(p => p.IsTracking)
        );
    }

    private static string SafeUrlDecode(string input)
    {
        if (string.IsNullOrEmpty(input)) return "";
        try
        {
            // Replace + with space first, then decode URL
            var withSpaces = input.Replace('+', ' ');
            return Uri.UnescapeDataString(withSpaces);
        }
        catch
        {
            return input;
        }
    }
}
