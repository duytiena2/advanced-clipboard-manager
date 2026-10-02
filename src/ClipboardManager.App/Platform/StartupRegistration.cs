using System;
using System.Threading.Tasks;
using Microsoft.Win32;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;

namespace ClipboardManager.App.Platform;

/// <summary>
/// "Start with Windows". Plain exe: HKCU\...\Run (per-user, no admin rights; Setup.exe writes the same value).
/// MSIX/Store: the package's StartupTask, because the Run key is virtualized inside a package.
/// </summary>
internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AdvancedClipboardManager";
    private const string BackgroundArg = "--background";

    /// <summary>Must match the TaskId in packaging\AppxManifest.xml.</summary>
    private const string PackagedTaskId = "ClipboardManagerStartup";

    public static bool IsEnabled()
    {
        if (PackageInfo.IsPackaged)
        {
            var state = Task.Run(async () => (await StartupTask.GetAsync(PackagedTaskId)).State).GetAwaiter().GetResult();
            return state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
        }
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        return key?.GetValue(ValueName) is string;
    }

    /// <summary>Returns whether startup is enabled afterwards (the user or a policy can block it for the packaged app).</summary>
    public static async Task<bool> SetEnabledAsync(bool enabled)
    {
        if (PackageInfo.IsPackaged)
        {
            var task = await StartupTask.GetAsync(PackagedTaskId);
            if (!enabled)
            {
                task.Disable();
                return false;
            }
            var state = await task.RequestEnableAsync();
            return state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
        }

        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true) ?? Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot resolve executable path.");
            key.SetValue(ValueName, $"\"{exe}\" {BackgroundArg}");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        return enabled;
    }

    /// <summary>True when Windows started the app at sign-in (Run key passes --background; a StartupTask passes no arguments).</summary>
    public static bool LaunchedAtStartup(string[] args)
    {
        if (Array.IndexOf(args, BackgroundArg) >= 0) return true;
        if (!PackageInfo.IsPackaged) return false;
        try { return AppInstance.GetActivatedEventArgs()?.Kind == ActivationKind.StartupTask; }
        catch (Exception) { return false; }
    }
}
