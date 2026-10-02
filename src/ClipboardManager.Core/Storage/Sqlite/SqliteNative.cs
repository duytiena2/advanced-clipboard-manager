using System.Reflection;
using System.Runtime.InteropServices;

namespace ClipboardManager.Core.Storage.Sqlite;

/// <summary>
/// Minimal P/Invoke surface for the SQLite C API. Library resolution order:
/// sqlite3 next to the app (official build, includes FTS5) → e_sqlite3 → Windows' winsqlite3.dll → libsqlite3.
/// </summary>
internal static class SqliteNative
{
    private const string Lib = "sqlite3";

    public const int SQLITE_OK = 0;
    public const int SQLITE_ROW = 100;
    public const int SQLITE_DONE = 101;
    public const int SQLITE_NULL = 5;
    public const int SQLITE_OPEN_READWRITE = 0x02;
    public const int SQLITE_OPEN_CREATE = 0x04;
    public const int SQLITE_OPEN_FULLMUTEX = 0x10000;
    public static readonly IntPtr SQLITE_TRANSIENT = new(-1);

    static SqliteNative()
    {
        NativeLibrary.SetDllImportResolver(typeof(SqliteNative).Assembly, Resolve);
    }

    /// <summary>Forces the static constructor (and so the resolver) to run.</summary>
    public static void EnsureInitialized() { }

    private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? path)
    {
        if (name != Lib) return IntPtr.Zero;
        string[] candidates = OperatingSystem.IsWindows()
            ? new[] { "sqlite3", "e_sqlite3", "winsqlite3" }
            : OperatingSystem.IsMacOS()
                ? new[] { "libsqlite3.dylib", "/usr/lib/libsqlite3.dylib", "sqlite3" }
                : new[] { "libsqlite3.so.0", "libsqlite3.so", "sqlite3" };
        foreach (var c in candidates)
        {
            if (NativeLibrary.TryLoad(c, assembly, DllImportSearchPath.SafeDirectories | DllImportSearchPath.ApplicationDirectory, out var h)) return h;
            if (NativeLibrary.TryLoad(c, out h)) return h;
        }
        return IntPtr.Zero;
    }

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int sqlite3_open_v2(byte[] filename, out IntPtr db, int flags, IntPtr vfs);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int sqlite3_close_v2(IntPtr db);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int nBytes, out IntPtr stmt, IntPtr tail);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int sqlite3_step(IntPtr stmt);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int sqlite3_finalize(IntPtr stmt);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int sqlite3_bind_text(IntPtr stmt, int index, byte[] value, int nBytes, IntPtr destructor);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int sqlite3_bind_int64(IntPtr stmt, int index, long value);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int sqlite3_bind_double(IntPtr stmt, int index, double value);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int sqlite3_bind_null(IntPtr stmt, int index);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int sqlite3_column_count(IntPtr stmt);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int sqlite3_column_type(IntPtr stmt, int col);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern long sqlite3_column_int64(IntPtr stmt, int col);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern double sqlite3_column_double(IntPtr stmt, int col);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr sqlite3_column_text(IntPtr stmt, int col);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int sqlite3_column_bytes(IntPtr stmt, int col);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr sqlite3_errmsg(IntPtr db);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern long sqlite3_last_insert_rowid(IntPtr db);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int sqlite3_changes(IntPtr db);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int sqlite3_busy_timeout(IntPtr db, int ms);

    public static byte[] Utf8Z(string s)
    {
        var bytes = new byte[System.Text.Encoding.UTF8.GetByteCount(s) + 1];
        System.Text.Encoding.UTF8.GetBytes(s, 0, s.Length, bytes, 0);
        return bytes;
    }
}
