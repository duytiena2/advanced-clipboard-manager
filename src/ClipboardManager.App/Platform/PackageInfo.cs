using System;
using System.IO;
using System.Runtime.InteropServices;
using ClipboardManager.Core.Services;

namespace ClipboardManager.App.Platform;

/// <summary>Whether the app runs as an MSIX package (Microsoft Store) or as a plain exe (Setup.exe / portable).</summary>
internal static class PackageInfo
{
    private const int APPMODEL_ERROR_NO_PACKAGE = 15700;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, char[]? packageFullName);

    public static bool IsPackaged { get; } = DetectPackaged();

    /// <summary>
    /// Packaged: the package's own LocalState folder. Writes to %LOCALAPPDATA% would be silently redirected
    /// there anyway, but then "Open data folder" would show an empty folder. Unpackaged: %LOCALAPPDATA%\ClipboardManager.
    /// </summary>
    public static string DataFolder { get; } = IsPackaged
        ? Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "ClipboardManager")
        : ClipboardService.DefaultDataFolder();

    private static bool DetectPackaged()
    {
        int length = 0;
        return GetCurrentPackageFullName(ref length, null) != APPMODEL_ERROR_NO_PACKAGE;
    }
}
