using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ClipboardManager.Mac.Platform;

public sealed record MacUpdateInfo(string TagName, Version Version, string ReleaseUrl, string? DownloadUrl, string? Body);

internal static class MacUpdateChecker
{
    private const string RepoOwner = "duytiena2";
    private const string RepoName = "advanced-clipboard-manager";
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(15) };

    public static Version CurrentVersion
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            return v is not null ? new Version(v.Major, v.Minor, Math.Max(0, v.Build)) : new Version(0, 1, 0);
        }
    }

    public static string CurrentVersionString => $"{CurrentVersion.Major}.{CurrentVersion.Minor}.{CurrentVersion.Build}";

    public static async Task<MacUpdateInfo?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest");
            req.Headers.UserAgent.Add(new ProductInfoHeaderValue("AdvancedClipboardManager", CurrentVersionString));
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));

            using var res = await HttpClient.SendAsync(req, cancellationToken).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) return null;

            var json = await res.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("tag_name", out var tagProp)) return null;
            var tag = tagProp.GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(tag)) return null;

            var cleanTag = tag.TrimStart('v', 'V');
            int dash = cleanTag.IndexOf('-');
            var versionPart = dash > 0 ? cleanTag[..dash] : cleanTag;

            if (!Version.TryParse(versionPart, out var remoteVersion))
            {
                if (!Version.TryParse(versionPart + ".0", out remoteVersion)) return null;
            }

            var current = CurrentVersion;
            if (remoteVersion <= current) return null;

            var htmlUrl = root.TryGetProperty("html_url", out var htmlProp)
                ? htmlProp.GetString() ?? $"https://github.com/{RepoOwner}/{RepoName}/releases/latest"
                : $"https://github.com/{RepoOwner}/{RepoName}/releases/latest";

            var body = root.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() : null;

            string? downloadUrl = null;
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                    if (name != null && (name.EndsWith(".dmg", StringComparison.OrdinalIgnoreCase) ||
                                         name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))
                    {
                        downloadUrl = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                        if (name.EndsWith(".dmg", StringComparison.OrdinalIgnoreCase)) break;
                    }
                }
            }

            return new MacUpdateInfo(tag, remoteVersion, htmlUrl, downloadUrl, body);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MacUpdateChecker] Error: {ex.Message}");
            return null;
        }
    }

    public static void OpenReleaseUrl(string url)
    {
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                Process.Start("open", url);
            }
            else
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
        }
        catch { }
    }
}
