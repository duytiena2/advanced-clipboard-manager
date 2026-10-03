using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ClipboardManager.App.Platform;

public sealed record UpdateInfo(string TagName, Version Version, string ReleaseUrl, string? DownloadUrl, string? Body);

/// <summary>Checks GitHub Releases for newer versions of the app.</summary>
internal static class UpdateChecker
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

    public static async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
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
                    if (name != null && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        if (name.Contains("Setup", StringComparison.OrdinalIgnoreCase))
                        {
                            downloadUrl = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                            break;
                        }
                        downloadUrl ??= asset.TryGetProperty("browser_download_url", out var u2) ? u2.GetString() : null;
                    }
                }
            }

            return new UpdateInfo(tag, remoteVersion, htmlUrl, downloadUrl, body);
        }
        catch (Exception ex)
        {
            App.Log(ex);
            return null;
        }
    }

    public static async Task<string?> DownloadInstallerAsync(UpdateInfo update, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(update.DownloadUrl)) return null;

        try
        {
            var tempDir = Path.GetTempPath();
            var fileName = $"AdvancedClipboardManager-Setup-{update.TagName}.exe";
            var tempFilePath = Path.Combine(tempDir, fileName);

            using var response = await HttpClient.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, useAsync: true);

            var buffer = new byte[8192];
            long totalRead = 0;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken).ConfigureAwait(false);
                totalRead += bytesRead;
                if (totalBytes > 0 && progress is not null)
                {
                    progress.Report((int)((totalRead * 100) / totalBytes));
                }
            }

            return tempFilePath;
        }
        catch (Exception ex)
        {
            App.Log(ex);
            return null;
        }
    }
}
