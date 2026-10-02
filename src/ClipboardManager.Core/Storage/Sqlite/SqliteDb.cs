using System.Runtime.InteropServices;
using System.Text;
using static ClipboardManager.Core.Storage.Sqlite.SqliteNative;

namespace ClipboardManager.Core.Storage.Sqlite;

public sealed class SqliteException : Exception
{
    public int Code { get; }
    public SqliteException(int code, string message) : base($"SQLite error {code}: {message}") => Code = code;
}

/// <summary>Read-only view over the current result row.</summary>
public readonly struct SqliteRow
{
    private readonly IntPtr _stmt;
    internal SqliteRow(IntPtr stmt) => _stmt = stmt;

    public bool IsNull(int col) => sqlite3_column_type(_stmt, col) == SQLITE_NULL;
    public long GetInt64(int col) => sqlite3_column_int64(_stmt, col);
    public int GetInt32(int col) => (int)sqlite3_column_int64(_stmt, col);
    public bool GetBool(int col) => sqlite3_column_int64(_stmt, col) != 0;
    public double GetDouble(int col) => sqlite3_column_double(_stmt, col);
    public long? GetInt64OrNull(int col) => IsNull(col) ? null : GetInt64(col);

    public string? GetStringOrNull(int col)
    {
        if (IsNull(col)) return null;
        var ptr = sqlite3_column_text(_stmt, col);
        int len = sqlite3_column_bytes(_stmt, col);
        return ptr == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(ptr, len);
    }

    public string GetString(int col) => GetStringOrNull(col) ?? "";
}

/// <summary>
/// Thin, thread-safe wrapper over one SQLite connection. All calls are serialized with a lock,
/// which is plenty for a single-user desktop app.
/// </summary>
public sealed class SqliteDb : IDisposable
{
    private IntPtr _db;
    private readonly object _gate = new();

    public SqliteDb(string path)
    {
        SqliteNative.EnsureInitialized();
        int rc;
        try
        {
            rc = sqlite3_open_v2(Utf8Z(path), out _db, SQLITE_OPEN_READWRITE | SQLITE_OPEN_CREATE | SQLITE_OPEN_FULLMUTEX, IntPtr.Zero);
        }
        catch (DllNotFoundException ex)
        {
            throw new InvalidOperationException(
                "SQLite native library not found. Place sqlite3.dll next to the app (build.ps1 downloads it) " +
                "or make sure winsqlite3.dll / libsqlite3 is available.", ex);
        }
        if (rc != SQLITE_OK) throw new SqliteException(rc, "cannot open database " + path);
        sqlite3_busy_timeout(_db, 3000);
        Execute("PRAGMA journal_mode=WAL;");
        Execute("PRAGMA foreign_keys=ON;");
    }

    public object SyncRoot => _gate;

    public int Execute(string sql, params object?[] args)
    {
        lock (_gate)
        {
            var stmt = Prepare(sql, args);
            try
            {
                int rc;
                while ((rc = sqlite3_step(stmt)) == SQLITE_ROW) { }
                if (rc != SQLITE_DONE) Throw(rc);
                return sqlite3_changes(_db);
            }
            finally
            {
                sqlite3_finalize(stmt);
            }
        }
    }

    public List<T> Query<T>(string sql, Func<SqliteRow, T> map, params object?[] args)
    {
        lock (_gate)
        {
            var stmt = Prepare(sql, args);
            var list = new List<T>();
            try
            {
                int rc;
                while ((rc = sqlite3_step(stmt)) == SQLITE_ROW) list.Add(map(new SqliteRow(stmt)));
                if (rc != SQLITE_DONE) Throw(rc);
                return list;
            }
            finally
            {
                sqlite3_finalize(stmt);
            }
        }
    }

    public T? Scalar<T>(string sql, Func<SqliteRow, T> map, params object?[] args)
    {
        var rows = Query(sql, map, args);
        return rows.Count > 0 ? rows[0] : default;
    }

    public long LastInsertRowId
    {
        get { lock (_gate) return sqlite3_last_insert_rowid(_db); }
    }

    /// <summary>Runs <paramref name="work"/> inside a transaction (re-entrant lock held throughout).</summary>
    public T InTransaction<T>(Func<T> work)
    {
        lock (_gate)
        {
            Execute("BEGIN IMMEDIATE;");
            try
            {
                var result = work();
                Execute("COMMIT;");
                return result;
            }
            catch
            {
                try { Execute("ROLLBACK;"); } catch { /* ignore */ }
                throw;
            }
        }
    }

    /// <summary>True when the loaded SQLite build supports FTS5.</summary>
    public bool SupportsFts5()
    {
        try
        {
            Execute("CREATE VIRTUAL TABLE IF NOT EXISTS temp.__fts5_probe USING fts5(x);");
            Execute("DROP TABLE IF EXISTS temp.__fts5_probe;");
            return true;
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    private IntPtr Prepare(string sql, object?[] args)
    {
        var bytes = Utf8Z(sql);
        int rc = sqlite3_prepare_v2(_db, bytes, bytes.Length, out var stmt, IntPtr.Zero);
        if (rc != SQLITE_OK) Throw(rc);
        for (int i = 0; i < args.Length; i++)
        {
            int idx = i + 1;
            rc = args[i] switch
            {
                null => sqlite3_bind_null(stmt, idx),
                string s => BindText(stmt, idx, s),
                bool b => sqlite3_bind_int64(stmt, idx, b ? 1 : 0),
                int n => sqlite3_bind_int64(stmt, idx, n),
                long n => sqlite3_bind_int64(stmt, idx, n),
                double d => sqlite3_bind_double(stmt, idx, d),
                DateTimeOffset dt => sqlite3_bind_int64(stmt, idx, dt.ToUnixTimeMilliseconds()),
                Enum e => BindText(stmt, idx, e.ToString()),
                _ => BindText(stmt, idx, args[i]!.ToString() ?? ""),
            };
            if (rc != SQLITE_OK)
            {
                sqlite3_finalize(stmt);
                Throw(rc);
            }
        }
        return stmt;
    }

    private static int BindText(IntPtr stmt, int idx, string s)
    {
        var b = Encoding.UTF8.GetBytes(s);
        return sqlite3_bind_text(stmt, idx, b, b.Length, SQLITE_TRANSIENT);
    }

    private void Throw(int rc)
    {
        var msg = Marshal.PtrToStringUTF8(sqlite3_errmsg(_db)) ?? "unknown";
        throw new SqliteException(rc, msg);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_db != IntPtr.Zero)
            {
                sqlite3_close_v2(_db);
                _db = IntPtr.Zero;
            }
        }
    }
}
