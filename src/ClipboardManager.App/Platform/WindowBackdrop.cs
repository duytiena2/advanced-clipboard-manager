using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace ClipboardManager.App.Platform;

/// <summary>
/// Applies modern OS backdrops (Windows 11 Acrylic / Mica, Windows 10 blur-behind) and rounded corners.
/// </summary>
internal static class WindowBackdrop
{
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMWCP_ROUND = 2;
    private const int DWMSBT_NONE = 1;
    private const int DWMSBT_TRANSIENTWINDOW = 3; // Acrylic

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS pMarInset);

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS
    {
        public int cxLeftWidth, cxRightWidth, cyTopHeight, cyBottomHeight;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

    private const int WCA_ACCENT_POLICY = 19;
    private const int ACCENT_DISABLED = 0;
    private const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;

    public static void Apply(Window window, bool enableAcrylic)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();

        var source = HwndSource.FromHwnd(hwnd);
        if (source?.CompositionTarget != null)
        {
            source.CompositionTarget.BackgroundColor = Colors.Transparent;
        }

        var margins = new MARGINS { cxLeftWidth = -1, cxRightWidth = -1, cyTopHeight = -1, cyBottomHeight = -1 };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);

        int corner = DWMWCP_ROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));

        if (enableAcrylic)
        {
            int backdrop = DWMSBT_TRANSIENTWINDOW;
            int hr = DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
            if (hr != 0)
            {
                ApplyWin10Accent(hwnd, ACCENT_ENABLE_ACRYLICBLURBEHIND);
            }
        }
        else
        {
            int backdrop = DWMSBT_NONE;
            DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
            ApplyWin10Accent(hwnd, ACCENT_DISABLED);
        }
    }

    private static void ApplyWin10Accent(IntPtr hwnd, int accentState)
    {
        try
        {
            var policy = new AccentPolicy
            {
                AccentState = accentState,
                AccentFlags = 2,
                GradientColor = 0x01FFFFFF
            };
            int size = Marshal.SizeOf(policy);
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(policy, ptr, false);
                var data = new WindowCompositionAttributeData
                {
                    Attribute = WCA_ACCENT_POLICY,
                    Data = ptr,
                    SizeOfData = size
                };
                SetWindowCompositionAttribute(hwnd, ref data);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
        catch
        {
            // Silently ignore if not supported
        }
    }
}
