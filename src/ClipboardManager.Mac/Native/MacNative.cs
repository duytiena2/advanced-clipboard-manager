using System;
using System.Runtime.InteropServices;

namespace ClipboardManager.Mac.Native;

internal static class MacNative
{
    private const string ObjCLib = "/usr/lib/libobjc.A.dylib";
    private const string CGLib = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CFLib = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    private const string SystemLib = "/usr/lib/libSystem.B.dylib";

    [DllImport(SystemLib, CharSet = CharSet.Ansi)]
    public static extern IntPtr dlopen(string path, int mode);

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
    public static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2, IntPtr arg3);

    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool objc_msgSend_bool_ptr_outptr(IntPtr receiver, IntPtr selector, IntPtr arg1, out IntPtr arg2);

    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    public static extern long objc_msgSend_long(IntPtr receiver, IntPtr selector);

    [DllImport(ObjCLib)]
    public static extern IntPtr objc_autoreleasePoolPush();

    [DllImport(ObjCLib)]
    public static extern void objc_autoreleasePoolPop(IntPtr pool);

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

    public static IntPtr CreateNSString(string str)
    {
        if (!OperatingSystem.IsMacOS() || str is null) return IntPtr.Zero;
        var cls = objc_getClass("NSString");
        var sel = sel_registerName("stringWithUTF8String:");
        var utf8 = Marshal.StringToCoTaskMemUTF8(str);
        try
        {
            return objc_msgSend(cls, sel, utf8);
        }
        finally
        {
            Marshal.FreeCoTaskMem(utf8);
        }
    }

    public static string? GetStringFromNSString(IntPtr nsString)
    {
        if (nsString == IntPtr.Zero || !OperatingSystem.IsMacOS()) return null;
        var sel = sel_registerName("UTF8String");
        var ptr = objc_msgSend(nsString, sel);
        return ptr == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(ptr);
    }
}
