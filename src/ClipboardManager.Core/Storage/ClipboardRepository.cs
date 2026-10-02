using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ClipboardManager.Core.Models;
using ClipboardManager.Core.Platform;
using ClipboardManager.Core.Search;
using ClipboardManager.Core.Storage.Sqlite;

namespace ClipboardManager.Core.Storage;

/// <summary>
/// SQLite persistence for clipboard items, with FTS5 search (falls back to LIKE if FTS5 is missing).
/// <para>
/// Optional encryption (<see cref="IsEncrypted"/>): title, text, formatting and metadata are encrypted per field with
/// <see cref="IDataProtector"/>, and the full-text index lives in an attached in-memory database rebuilt at startup,
/// so no plaintext copy of the content is written to disk.
/// </para>
/// </summary>
public sealed class ClipboardRepository : IDisposable
{
    private const int SchemaVersion = 3;
    private const string EncryptedPrefix = "enc1:";
    private readonly SqliteDb _db;
    private readonly IDataProtector? _protector;
    private bool _encrypted;
    private byte[]? _hashKey;

    public bool FullTextEnabled { get; }
    public bool IsEncrypted => _encrypted;

    /// <summary>Full-text table: on disk normally, in memory when encrypted.</summary>
    private string Fts => _encrypted ? "mem.clipboard_search" : "main.clipboard_search";

    // html_content / rtf_content can be large, so lists only load a flag; the formats are read by GetRichText.
    private const string Columns =
        "id, content_type, subtype, title, text_content, binary_path, content_hash, size_bytes, created_at, " +
        "last_copied_at, accessed_at, expires_at, is_pinned, is_sensitive, copy_count, workspace, source_application, " +
        "detection_confidence, metadata_json, (html_content IS NOT NULL OR rtf_content IS NOT NULL)";

    /// <param name="enableFullText">false forces the LIKE fallback (used by tests; also what happens when FTS5 is missing).</param>
    /// <param name="protector">Needed to open an encrypted database or to turn encryption on.</param>
    public ClipboardRepository(string databasePath, bool enableFullText = true, IDataProtector? protector = null)
    {
        _db = new SqliteDb(databasePath);
        _protector = protector;
        FullTextEnabled = enableFullText && _db.SupportsFts5();
        Migrate();
    }

    private void Migrate()
    {
        _db.Execute(@"CREATE TABLE IF NOT EXISTS clipboard_items (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            content_type TEXT NOT NULL,
            subtype TEXT NOT NULL DEFAULT '',
            title TEXT NOT NULL DEFAULT '',
            text_content TEXT NULL,
            binary_path TEXT NULL,
            content_hash TEXT NOT NULL,
            size_bytes INTEGER NOT NULL DEFAULT 0,
            created_at INTEGER NOT NULL,
            last_copied_at INTEGER NOT NULL,
            accessed_at INTEGER NULL,
            expires_at INTEGER NULL,
            is_pinned INTEGER NOT NULL DEFAULT 0,
            is_sensitive INTEGER NOT NULL DEFAULT 0,
            copy_count INTEGER NOT NULL DEFAULT 1,
            workspace TEXT NOT NULL DEFAULT 'Default',
            source_application TEXT NULL,
            detection_confidence REAL NOT NULL DEFAULT 0,
            metadata_json TEXT NULL
        );");
        // v2: formatting captured alongside text.
        AddColumnIfMissing("html_content", "TEXT NULL");
        AddColumnIfMissing("rtf_content", "TEXT NULL");
        _db.Execute("CREATE UNIQUE INDEX IF NOT EXISTS ux_items_hash_ws ON clipboard_items(content_hash, workspace);");
        _db.Execute("CREATE INDEX IF NOT EXISTS ix_items_last_copied ON clipboard_items(last_copied_at DESC);");
        _db.Execute("CREATE INDEX IF NOT EXISTS ix_items_expires ON clipboard_items(expires_at) WHERE expires_at IS NOT NULL;");
        _db.Execute("CREATE TABLE IF NOT EXISTS settings (key TEXT PRIMARY KEY, value TEXT NOT NULL);");

        // v3: optional encryption.
        _db.Execute("ATTACH DATABASE ':memory:' AS mem;");
        _encrypted = GetSetting("encrypted") == "1";
        if (_encrypted)
        {
            if (_protector is null) throw new InvalidOperationException("The clipboard database is encrypted, but no data protector is available.");
            _hashKey = UnprotectBase64(GetSetting("hash_key") ?? throw new InvalidOperationException("Encrypted database has no hash key."));
        }
        CreateSearchIndex();

        SetSetting("schema_version", SchemaVersion.ToString());
    }

    private void CreateSearchIndex()
    {
        if (!FullTextEnabled) return;
        _db.Execute($"CREATE VIRTUAL TABLE IF NOT EXISTS {Fts} USING fts5(title, content, tokenize = 'unicode61 remove_diacritics 2');");
        if (_encrypted) RebuildSearchIndex();
    }

    private void RebuildSearchIndex()
    {
        _db.InTransaction(() =>
        {
            _db.Execute($"DELETE FROM {Fts};");
            foreach (var item in _db.Query($"SELECT {Columns} FROM clipboard_items;", Map)) IndexForSearch(item);
            return 0;
        });
    }

    private void AddColumnIfMissing(string column, string definition)
    {
        var existing = _db.Query("PRAGMA main.table_info(clipboard_items);", r => r.GetString(1));
        if (!existing.Contains(column, StringComparer.OrdinalIgnoreCase))
            _db.Execute($"ALTER TABLE clipboard_items ADD COLUMN {column} {definition};");
    }

    private string? GetSetting(string key) =>
        _db.Scalar("SELECT value FROM main.settings WHERE key = ?;", r => r.GetString(0), key);

    private void SetSetting(string key, string value) =>
        _db.Execute("INSERT INTO main.settings(key, value) VALUES(?, ?) ON CONFLICT(key) DO UPDATE SET value = excluded.value;", key, value);

    // ---- Encryption ----

    /// <summary>
    /// Content hash used for de-duplication. When encrypted it is an HMAC with a per-database secret key, so the stored
    /// hashes cannot be used to confirm a guessed value (e.g. a short password).
    /// </summary>
    public string Hash(byte[] data) =>
        Convert.ToHexString(_hashKey is null ? SHA256.HashData(data) : HMACSHA256.HashData(_hashKey, data)).ToLowerInvariant();

    public byte[] ProtectBytes(byte[] data) => _protector?.Protect(data) ?? throw new InvalidOperationException("No data protector.");
    public byte[] UnprotectBytes(byte[] data) => _protector?.Unprotect(data) ?? throw new InvalidOperationException("No data protector.");

    private string? Protect(string? value)
    {
        if (!_encrypted || value is null) return value;
        return EncryptedPrefix + Convert.ToBase64String(_protector!.Protect(Encoding.UTF8.GetBytes(value)));
    }

    private string? Unprotect(string? value) =>
        value is not null && value.StartsWith(EncryptedPrefix, StringComparison.Ordinal)
            ? Encoding.UTF8.GetString(UnprotectBase64(value[EncryptedPrefix.Length..]))
            : value;

    private byte[] UnprotectBase64(string base64)
    {
        if (_protector is null) throw new InvalidOperationException("The clipboard database is encrypted, but no data protector is available.");
        try
        {
            return _protector.Unprotect(Convert.FromBase64String(base64));
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException(
                "The clipboard database was encrypted by another Windows account or computer and cannot be read here.", ex);
        }
    }

    /// <summary>
    /// Turns encryption on or off, rewriting every row. <paramref name="rekey"/> receives each decrypted item (after the
    /// mode switch, so <see cref="Hash"/> already uses the new scheme) and returns its new content hash and binary path
    /// (the caller converts image files). Old plaintext pages are removed with VACUUM afterwards.
    /// </summary>
    public void SetEncrypted(bool on, Func<ClipboardItem, (string Hash, string? BinaryPath)> rekey)
    {
        if (on == _encrypted) return;
        if (on && _protector is null) throw new InvalidOperationException("Encryption needs a data protector.");

        var oldFts = Fts;
        var (wasEncrypted, oldKey) = (_encrypted, _hashKey);
        try
        {
            RewriteRows(on, rekey);
        }
        catch
        {
            (_encrypted, _hashKey) = (wasEncrypted, oldKey); // the transaction was rolled back
            throw;
        }

        if (FullTextEnabled)
        {
            _db.Execute($"DROP TABLE IF EXISTS {oldFts};");
            _db.Execute($"CREATE VIRTUAL TABLE IF NOT EXISTS {Fts} USING fts5(title, content, tokenize = 'unicode61 remove_diacritics 2');");
            RebuildSearchIndex();
        }
        // Rewrite the file so freed pages with old plaintext are gone, then flush the WAL.
        _db.Execute("VACUUM main;");
        _db.Execute("PRAGMA main.wal_checkpoint(TRUNCATE);");
    }

    private void RewriteRows(bool on, Func<ClipboardItem, (string Hash, string? BinaryPath)> rekey)
    {
        _db.InTransaction(() =>
        {
            var items = _db.Query($"SELECT {Columns} FROM clipboard_items;", Map); // decrypted with the current mode
            var rich = items.ToDictionary(i => i.Id, i => GetRichText(i.Id));

            _encrypted = on;
            if (on)
            {
                _hashKey = RandomNumberGenerator.GetBytes(32);
                SetSetting("hash_key", Convert.ToBase64String(_protector!.Protect(_hashKey)));
            }
            else
            {
                _hashKey = null;
                _db.Execute("DELETE FROM main.settings WHERE key = 'hash_key';");
            }
            SetSetting("encrypted", on ? "1" : "0");

            // Two passes so new hashes never collide with old ones on the (hash, workspace) unique index.
            foreach (var item in items) _db.Execute("UPDATE clipboard_items SET content_hash = 'migrating:' || id WHERE id = ?;", item.Id);
            foreach (var item in items)
            {
                var (hash, path) = rekey(item);
                var r = rich[item.Id];
                _db.Execute("UPDATE clipboard_items SET title = ?, text_content = ?, metadata_json = ?, html_content = ?, rtf_content = ?, content_hash = ?, binary_path = ? WHERE id = ?;",
                    Protect(item.Title), Protect(item.TextContent), Protect(item.MetadataJson), Protect(r?.Html), Protect(r?.Rtf), hash, path, item.Id);
            }
            return 0;
        });
    }

    // ---- Items ----

    /// <summary>
    /// Inserts a new item, or — if the same content already exists in the workspace — bumps its copy count
    /// and recency instead of creating a duplicate. Returns the stored item and whether it was new.
    /// </summary>
    /// <param name="rich">Formatting copied with the text. On a re-copy it replaces the stored one (the latest copy wins).</param>
    public (ClipboardItem Item, bool IsNew) AddOrTouch(ClipboardItem item, RichText? rich = null)
    {
        var html = string.IsNullOrEmpty(rich?.Html) ? null : rich!.Html;
        var rtf = string.IsNullOrEmpty(rich?.Rtf) ? null : rich!.Rtf;
        return _db.InTransaction(() =>
        {
            var existing = _db.Scalar($"SELECT {Columns} FROM clipboard_items WHERE content_hash = ? AND workspace = ?;",
                Map, item.ContentHash, item.Workspace);
            if (existing is not null)
            {
                // Keep the existing (possibly user-adjusted) expiry if pinned; otherwise extend to the new one.
                var newExpiry = existing.IsPinned ? existing.ExpiresAt : item.ExpiresAt;
                _db.Execute("UPDATE clipboard_items SET copy_count = copy_count + 1, last_copied_at = ?, expires_at = ?, source_application = COALESCE(?, source_application), html_content = ?, rtf_content = ? WHERE id = ?;",
                    item.LastCopiedAt, (object?)newExpiry, item.SourceApplication, Protect(html), Protect(rtf), existing.Id);
                existing.CopyCount += 1;
                existing.LastCopiedAt = item.LastCopiedAt;
                existing.ExpiresAt = newExpiry;
                existing.HasRichText = html is not null || rtf is not null;
                if (item.SourceApplication is not null) existing.SourceApplication = item.SourceApplication;
                return (existing, false);
            }

            _db.Execute($@"INSERT INTO clipboard_items (content_type, subtype, title, text_content, binary_path, content_hash, size_bytes,
                created_at, last_copied_at, accessed_at, expires_at, is_pinned, is_sensitive, copy_count, workspace, source_application,
                detection_confidence, metadata_json, html_content, rtf_content) VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?);",
                item.Kind, item.Subtype, Protect(item.Title), Protect(item.TextContent), item.BinaryPath, item.ContentHash, item.SizeBytes,
                item.CreatedAt, item.LastCopiedAt, (object?)item.AccessedAt, (object?)item.ExpiresAt, item.IsPinned, item.IsSensitive,
                item.CopyCount, item.Workspace, item.SourceApplication, item.Confidence, Protect(item.MetadataJson), Protect(html), Protect(rtf));
            item.Id = _db.LastInsertRowId;
            item.HasRichText = html is not null || rtf is not null;
            IndexForSearch(item);
            return (item, true);
        });
    }

    private void IndexForSearch(ClipboardItem item)
    {
        if (!FullTextEnabled) return;
        // Sensitive values are never put in the full-text index.
        var content = item.IsSensitive ? "" : item.TextContent ?? "";
        _db.Execute($"INSERT INTO {Fts}(rowid, title, content) VALUES (?, ?, ?);", item.Id, item.Title, content);
    }

    public ClipboardItem? Get(long id) =>
        _db.Scalar($"SELECT {Columns} FROM clipboard_items WHERE id = ?;", Map, id);

    public RichText? GetRichText(long id) =>
        _db.Scalar("SELECT html_content, rtf_content FROM clipboard_items WHERE id = ?;",
            r => new RichText(Unprotect(r.GetStringOrNull(0)), Unprotect(r.GetStringOrNull(1))), id);

    /// <summary>Most recent first; pinned items first when <paramref name="pinnedFirst"/> is true.</summary>
    public List<ClipboardItem> Search(SearchQuery query, int limit = 200, bool pinnedFirst = true)
    {
        var where = new List<string>();
        var args = new List<object?>();
        const string from = "clipboard_items i";
        // Encrypted without FTS5: the columns can't be matched in SQL, so terms are matched after decrypting.
        bool filterInMemory = query.Terms.Count > 0 && !FullTextEnabled && _encrypted;

        if (query.Terms.Count > 0 && !filterInMemory)
        {
            if (FullTextEnabled)
            {
                where.Add($"i.id IN (SELECT rowid FROM {Fts} WHERE clipboard_search MATCH ?)");
                args.Add(BuildFtsExpression(query.Terms));
            }
            else
            {
                foreach (var term in query.Terms)
                {
                    where.Add("(i.title LIKE ? ESCAPE '\\' OR (i.is_sensitive = 0 AND i.text_content LIKE ? ESCAPE '\\'))");
                    var like = "%" + EscapeLike(term) + "%";
                    args.Add(like);
                    args.Add(like);
                }
            }
        }
        if (query.Kind is not null) { where.Add("i.content_type = ?"); args.Add(query.Kind.Value.ToString()); }
        if (query.Subtype is not null) { where.Add("i.subtype = ?"); args.Add(query.Subtype); }
        if (query.Workspace is not null) { where.Add("i.workspace = ? COLLATE NOCASE"); args.Add(query.Workspace); }
        if (query.Pinned is not null) { where.Add("i.is_pinned = ?"); args.Add(query.Pinned.Value); }
        if (query.Sensitive is not null) { where.Add("i.is_sensitive = ?"); args.Add(query.Sensitive.Value); }
        if (query.Before is not null) { where.Add("i.last_copied_at < ?"); args.Add(query.Before.Value); }
        if (query.After is not null) { where.Add("i.last_copied_at >= ?"); args.Add(query.After.Value); }

        var sql = new StringBuilder($"SELECT {Columns} FROM {from}");
        if (where.Count > 0) sql.Append(" WHERE ").Append(string.Join(" AND ", where));
        sql.Append(pinnedFirst ? " ORDER BY i.is_pinned DESC, i.last_copied_at DESC" : " ORDER BY i.last_copied_at DESC");
        if (!filterInMemory)
        {
            sql.Append(" LIMIT ?");
            args.Add(limit);
        }
        var rows = _db.Query(sql.Append(';').ToString(), Map, args.ToArray());
        return filterInMemory ? rows.Where(i => MatchesAllTerms(i, query.Terms)).Take(limit).ToList() : rows;
    }

    private static bool MatchesAllTerms(ClipboardItem item, List<string> terms)
    {
        var compare = CultureInfo.InvariantCulture.CompareInfo;
        const CompareOptions opts = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
        return terms.All(t => compare.IndexOf(item.Title, t, opts) >= 0 ||
                              (!item.IsSensitive && item.TextContent is not null && compare.IndexOf(item.TextContent, t, opts) >= 0));
    }

    public List<ClipboardItem> Recent(int limit = 200) => Search(new SearchQuery(), limit);

    public void SetPinned(long id, bool pinned)
    {
        // Pinned items never expire (unless the user later unpins them; expiry is then recomputed by the caller).
        if (pinned) _db.Execute("UPDATE clipboard_items SET is_pinned = 1, expires_at = NULL WHERE id = ?;", id);
        else _db.Execute("UPDATE clipboard_items SET is_pinned = 0 WHERE id = ?;", id);
    }

    public void SetExpiry(long id, DateTimeOffset? expiresAt) =>
        _db.Execute("UPDATE clipboard_items SET expires_at = ? WHERE id = ?;", (object?)expiresAt, id);

    public void MarkAccessed(long id, DateTimeOffset when) =>
        _db.Execute("UPDATE clipboard_items SET accessed_at = ? WHERE id = ?;", when, id);

    public void MoveToWorkspace(long id, string workspace) =>
        _db.Execute("UPDATE OR IGNORE clipboard_items SET workspace = ? WHERE id = ?;", workspace, id);

    /// <returns>Binary payload path of the deleted item (to remove from disk), if any.</returns>
    public string? Delete(long id)
    {
        return _db.InTransaction(() =>
        {
            var path = _db.Scalar("SELECT binary_path FROM clipboard_items WHERE id = ?;", r => r.GetStringOrNull(0), id);
            _db.Execute("DELETE FROM clipboard_items WHERE id = ?;", id);
            if (FullTextEnabled) _db.Execute($"DELETE FROM {Fts} WHERE rowid = ?;", id);
            return path;
        });
    }

    /// <summary>Deletes every item whose expiry has passed. Pinned items are never touched.</summary>
    public List<string> DeleteExpired(DateTimeOffset now) =>
        DeleteWhere("is_pinned = 0 AND expires_at IS NOT NULL AND expires_at <= ?", now);

    /// <summary>Deletes all unpinned history items (snippets are kept).</summary>
    public List<string> ClearUnpinned() => DeleteWhere("is_pinned = 0 AND content_type <> 'Snippet'");

    /// <summary>Keeps at most <paramref name="maxItems"/> unpinned history items, deleting the oldest. Snippets don't count.</summary>
    public List<string> EnforceMaxItems(int maxItems)
    {
        if (maxItems <= 0) return new();
        return DeleteWhere(
            "is_pinned = 0 AND content_type <> 'Snippet' AND id NOT IN (SELECT id FROM clipboard_items WHERE is_pinned = 0 AND content_type <> 'Snippet' ORDER BY last_copied_at DESC LIMIT ?)",
            maxItems);
    }

    /// <summary>Replaces an item's title/text (snippet edits) and re-indexes it.</summary>
    public void UpdateContent(ClipboardItem item)
    {
        _db.InTransaction(() =>
        {
            _db.Execute("UPDATE clipboard_items SET title = ?, text_content = ?, content_hash = ?, size_bytes = ?, workspace = ? WHERE id = ?;",
                Protect(item.Title), Protect(item.TextContent), item.ContentHash, item.SizeBytes, item.Workspace, item.Id);
            if (FullTextEnabled)
            {
                _db.Execute($"DELETE FROM {Fts} WHERE rowid = ?;", item.Id);
                IndexForSearch(item);
            }
            return 0;
        });
    }

    /// <summary>Moves an item to the top of the recency order (used snippets float up).</summary>
    public void Touch(long id, DateTimeOffset when) =>
        _db.Execute("UPDATE clipboard_items SET last_copied_at = ?, accessed_at = ? WHERE id = ?;", when, when, id);

    public bool ExistsWithHash(string hash, string workspace, long exceptId) =>
        _db.Scalar("SELECT COUNT(*) FROM clipboard_items WHERE content_hash = ? AND workspace = ? AND id <> ?;", r => r.GetInt64(0), hash, workspace, exceptId) > 0;

    private List<string> DeleteWhere(string condition, params object?[] args)
    {
        return _db.InTransaction(() =>
        {
            var victims = _db.Query($"SELECT id, binary_path FROM clipboard_items WHERE {condition};",
                r => (Id: r.GetInt64(0), Path: r.GetStringOrNull(1)), args);
            foreach (var v in victims)
            {
                _db.Execute("DELETE FROM clipboard_items WHERE id = ?;", v.Id);
                if (FullTextEnabled) _db.Execute($"DELETE FROM {Fts} WHERE rowid = ?;", v.Id);
            }
            return victims.Where(v => v.Path is not null).Select(v => v.Path!).ToList();
        });
    }

    public int Count() => (int)_db.Scalar("SELECT COUNT(*) FROM clipboard_items;", r => r.GetInt64(0));

    public long TotalSizeBytes() => _db.Scalar("SELECT COALESCE(SUM(size_bytes), 0) FROM clipboard_items;", r => r.GetInt64(0));

    public List<(string Name, int Count)> Workspaces() =>
        _db.Query("SELECT workspace, COUNT(*) FROM clipboard_items GROUP BY workspace ORDER BY workspace;", r => (r.GetString(0), r.GetInt32(1)));

    /// <summary>FTS5 expression: each term quoted (so user input can't inject operators) with prefix matching.</summary>
    internal static string BuildFtsExpression(IEnumerable<string> terms) =>
        string.Join(" AND ", terms.Select(t => "\"" + t.Replace("\"", "\"\"") + "\"*"));

    private static string EscapeLike(string s) => s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private ClipboardItem Map(SqliteRow r) => new()
    {
        Id = r.GetInt64(0),
        Kind = Enum.TryParse<ContentKind>(r.GetString(1), out var k) ? k : ContentKind.Text,
        Subtype = r.GetString(2),
        Title = Unprotect(r.GetString(3))!,
        TextContent = Unprotect(r.GetStringOrNull(4)),
        BinaryPath = r.GetStringOrNull(5),
        ContentHash = r.GetString(6),
        SizeBytes = r.GetInt64(7),
        CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(8)),
        LastCopiedAt = DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(9)),
        AccessedAt = r.GetInt64OrNull(10) is { } a ? DateTimeOffset.FromUnixTimeMilliseconds(a) : null,
        ExpiresAt = r.GetInt64OrNull(11) is { } e ? DateTimeOffset.FromUnixTimeMilliseconds(e) : null,
        IsPinned = r.GetBool(12),
        IsSensitive = r.GetBool(13),
        CopyCount = r.GetInt32(14),
        Workspace = r.GetString(15),
        SourceApplication = r.GetStringOrNull(16),
        Confidence = r.GetDouble(17),
        MetadataJson = Unprotect(r.GetStringOrNull(18)),
        HasRichText = r.GetBool(19),
    };

    public void Dispose() => _db.Dispose();
}
