namespace ClipboardManager.Core.Services;

/// <summary>Short human-readable times for list rows ("2 min ago", "expires in 5 days").</summary>
public static class Humanize
{
    public static string Ago(DateTimeOffset when, DateTimeOffset now)
    {
        var d = now - when;
        if (d < TimeSpan.FromSeconds(45)) return "just now";
        if (d < TimeSpan.FromMinutes(60)) return $"{Math.Max(1, (int)Math.Round(d.TotalMinutes))} min ago";
        if (d < TimeSpan.FromHours(24)) return $"{(int)d.TotalHours} h ago";
        if (d < TimeSpan.FromDays(2)) return "yesterday";
        if (d < TimeSpan.FromDays(30)) return $"{(int)d.TotalDays} days ago";
        return when.ToLocalTime().ToString("yyyy-MM-dd");
    }

    public static string ExpiresIn(DateTimeOffset? expiresAt, DateTimeOffset now)
    {
        if (expiresAt is null) return "never expires";
        var d = expiresAt.Value - now;
        if (d <= TimeSpan.Zero) return "expired";
        if (d < TimeSpan.FromHours(1)) return $"expires in {(int)d.TotalMinutes:00}:{d.Seconds:00}";
        if (d < TimeSpan.FromDays(1)) return $"expires in {(int)Math.Ceiling(d.TotalHours)} h";
        return $"expires in {(int)Math.Ceiling(d.TotalDays)} days";
    }

    /// <summary>A retention setting in words: 0 → "never expires", 90 → "1.5 hours", 10080 → "7 days".</summary>
    public static string Minutes(int minutes)
    {
        if (minutes <= 0) return "never expires";
        if (minutes < 60) return minutes == 1 ? "1 minute" : $"{minutes} minutes";
        if (minutes < 60 * 24) return Plural(minutes / 60.0, "hour");
        return Plural(minutes / (60.0 * 24), "day");
    }

    private static string Plural(double n, string unit)
    {
        var s = n.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
        return s == "1" ? $"1 {unit}" : $"{s} {unit}s";
    }

    public static string Bytes(long n) => n switch
    {
        < 1024 => $"{n} B",
        < 1024 * 1024 => $"{n / 1024.0:0.#} KB",
        _ => $"{n / (1024.0 * 1024):0.#} MB",
    };
}
