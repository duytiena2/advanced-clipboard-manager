using ClipboardManager.Core.Models;

namespace ClipboardManager.Core.Services;

/// <summary>expires_at = created_at + retention(kind). Pinned items never expire.</summary>
public sealed class ExpirationPolicy
{
    private readonly AppSettings _settings;

    public ExpirationPolicy(AppSettings settings) => _settings = settings;

    public TimeSpan? RetentionFor(ContentKind kind, string subtype)
    {
        if (kind == ContentKind.Sensitive && subtype == "password" && _settings.RetentionMinutes.TryGetValue("password", out var pw))
            return pw > 0 ? TimeSpan.FromMinutes(pw) : null;
        return _settings.RetentionMinutes.TryGetValue(kind.ToString(), out var m) && m > 0 ? TimeSpan.FromMinutes(m) : null;
    }

    public DateTimeOffset? ExpiresAt(ClipboardItem item, DateTimeOffset from)
    {
        if (item.IsPinned) return null;
        var retention = RetentionFor(item.Kind, item.Subtype);
        return retention is null ? null : from + retention.Value;
    }
}
