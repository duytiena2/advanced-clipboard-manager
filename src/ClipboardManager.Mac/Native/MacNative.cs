using System;
using System.Runtime.InteropServices;

namespace ClipboardManager.Mac.Native;

internal static class MacNative
{
    private const string ObjCLib = "/usr/lib/libobjc.A.dylib";
    private const string CGLib = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CFLib = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    [DllImport(ObjCLib, CharSet = CharSet.Ansi)]
    public static extern IntPtr objc_getClass(string name);

    [DllImport(ObjCLib, CharSet = CharSet.Ansi)]
    public static extern IntPtr sel_registerName(string name);

    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    public static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    public static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector, IntPtr arg1);

    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    public static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2);

    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    public static extern long objc_msgSend_long(IntPtr receiver, IntPtr selector);

    // CoreGraphics for simulating paste (Cmd+V)
    public const int kCGHIDEventTap = 0;
    public const ulong kCGEventFlagMaskCommand = 0x00100000;
    public const ushort kVK_ANSI_V = 0x09;

    [DllImport(CGLib)]
    public static extern IntPtr CGEventCreateKeyboardEvent(IntPtr source, ushort virtualKey, bool keyDown);

    [DllImport(CGLib)]
    public static extern void CGEventSetFlags(IntPtr @event, ulong flags);

    [DllImport(CGLib)]
    public static extern void CGEventPost(int tap, IntPtr @event);

    [DllImport(CFLib)]
    public static extern void CFRelease(IntPtr cf);

    // Helpers
    public static IntPtr GetClass(string name) => OperatingSystem.IsMacOS() ? objc_getClass(name) : IntPtr.Zero;
    public static IntPtr GetSelector(string name) => OperatingSystem.IsMacOS() ? sel_registerName(name) : IntPtr.Zero;
}
