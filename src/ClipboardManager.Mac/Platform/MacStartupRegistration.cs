using System;
using System.Diagnostics;
using System.IO;

namespace ClipboardManager.Mac.Platform;

internal static class MacStartupRegistration
{
    private const string PlistFileName = "com.duytiena2.clipboardmanager.plist";

    private static string PlistPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Library", "LaunchAgents", PlistFileName);

    public static bool IsEnabled()
    {
        if (!OperatingSystem.IsMacOS()) return false;
        return File.Exists(PlistPath);
    }

    public static bool SetEnabled(bool enabled)
    {
        if (!OperatingSystem.IsMacOS()) return false;

        try
        {
            var path = PlistPath;
            if (!enabled)
            {
                if (File.Exists(path))
                {
                    try { Process.Start("launchctl", $"unload \"{path}\"")?.WaitForExit(); } catch { }
                    File.Delete(path);
                }
                return true;
            }

            var dir = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(dir);

            string exePath = Environment.ProcessPath ?? "";
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            {
                exePath = "/Applications/Advanced Clipboard Manager.app/Contents/MacOS/ClipboardManager";
            }

            string plistContent = $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
                <plist version="1.0">
                <dict>
                    <key>Label</key>
                    <string>com.duytiena2.clipboardmanager</string>
                    <key>ProgramArguments</key>
                    <array>
                        <string>{exePath}</string>
                    </array>
                    <key>RunAtLoad</key>
                    <true/>
                    <key>ProcessType</key>
                    <string>Interactive</string>
                </dict>
                </plist>
                """;

            File.WriteAllText(path, plistContent);
            try { Process.Start("launchctl", $"load \"{path}\"")?.WaitForExit(); } catch { }
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MacStartupRegistration] {ex.Message}");
            return false;
        }
    }
}
