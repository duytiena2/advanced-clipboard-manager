using System.Text.Json;
using System.Text.Json.Serialization;
using ClipboardManager.Core.Models;

namespace ClipboardManager.Core.Services;

/// <summary>User settings, persisted as JSON in the data folder (settings.json).</summary>
public sealed class AppSettings
{
    public bool CaptureEnabled { get; set; } = true;
    public string QuickPasteHotkey { get; set; } = "Ctrl+Shift+V";
    public int MaxItems { get; set; } = 5000;
    public long MaxImageBytes { get; set; } = 20 * 1024 * 1024;
    public long MaxTextChars { get; set; } = 1_000_000;
    public string DefaultWorkspace { get; set; } = "Default";

    // Privacy
    public bool DetectSensitive { get; set; } = true;
    public bool NeverStorePasswords { get; set; } = false;
    public bool NeverStorePrivateKeys { get; set; } = true;
    public bool BlurSensitive { get; set; } = true;
    public List<string> ExcludedApplications { get; set; } = new() { "1Password", "KeePass", "KeePassXC", "Bitwarden", "LastPass" };

    /// <summary>Retention per kind in minutes. Missing or ≤ 0 = never expires.</summary>
    public Dictionary<string, int> RetentionMinutes { get; set; } = DefaultRetention();

    public static Dictionary<string, int> DefaultRetention() => new()
    {
        [nameof(ContentKind.Sensitive)] = 5,
        ["password"] = 1,
        [nameof(ContentKind.Text)] = 60 * 24,          // 1 day
        [nameof(ContentKind.Code)] = 60 * 24 * 7,      // 7 days
        [nameof(ContentKind.Url)] = 60 * 24 * 7,
        [nameof(ContentKind.Email)] = 60 * 24 * 7,
        [nameof(ContentKind.Phone)] = 60 * 24 * 7,
        [nameof(ContentKind.Number)] = 60 * 24,
        [nameof(ContentKind.Image)] = 60,              // 1 hour
        [nameof(ContentKind.Files)] = 60 * 24,
    };

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static AppSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOpts);
                if (s is not null)
                {
                    // Fill retention keys added in newer versions.
                    foreach (var kv in DefaultRetention()) s.RetentionMinutes.TryAdd(kv.Key, kv.Value);
                    return s;
                }
            }
        }
        catch (JsonException)
        {
            // Corrupt file: fall back to defaults (the bad file is kept for inspection).
            try { File.Copy(path, path + ".bak", overwrite: true); } catch (IOException) { }
        }
        return new AppSettings();
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOpts));
        File.Move(tmp, path, overwrite: true);
    }
}
