using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using ClipboardManager.Core.Platform;

namespace ClipboardManager.App.Platform;

/// <summary>
/// Windows DPAPI (CryptProtectData, CurrentUser scope): data can only be decrypted by the same Windows account on this
/// machine. The key is derived from the user's logon credentials, so there is no password to type or store.
/// </summary>
internal sealed class DpapiProtector : IDataProtector
{
    // Mixed into the encryption so other apps calling DPAPI for this user can't read our blobs by accident.
    private static readonly byte[] Entropy = "AdvancedClipboardManager.v1"u8.ToArray();

    private const int CRYPTPROTECT_UI_FORBIDDEN = 0x1;

    public byte[] Protect(byte[] plain) => Run(plain, protect: true);

    public byte[] Unprotect(byte[] protectedData) => Run(protectedData, protect: false);

    private static byte[] Run(byte[] input, bool protect)
    {
        var inHandle = GCHandle.Alloc(input, GCHandleType.Pinned);
        var entropyHandle = GCHandle.Alloc(Entropy, GCHandleType.Pinned);
        var output = new DATA_BLOB();
        try
        {
            var inBlob = new DATA_BLOB { cbData = input.Length, pbData = inHandle.AddrOfPinnedObject() };
            var entropyBlob = new DATA_BLOB { cbData = Entropy.Length, pbData = entropyHandle.AddrOfPinnedObject() };
            bool ok = protect
                ? CryptProtectData(ref inBlob, null, ref entropyBlob, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, ref output)
                : CryptUnprotectData(ref inBlob, IntPtr.Zero, ref entropyBlob, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, ref output);
            if (!ok) throw new CryptographicException(Marshal.GetLastWin32Error());

            var result = new byte[output.cbData];
            Marshal.Copy(output.pbData, result, 0, output.cbData);
            return result;
        }
        finally
        {
            if (output.pbData != IntPtr.Zero)
            {
                if (!protect) ZeroMemory(output.pbData, (UIntPtr)output.cbData); // plaintext
                LocalFree(output.pbData);
            }
            inHandle.Free();
            entropyHandle.Free();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DATA_BLOB
    {
        public int cbData;
        public IntPtr pbData;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref DATA_BLOB pDataIn, string? szDataDescr, ref DATA_BLOB pOptionalEntropy,
        IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DATA_BLOB pDataOut);

    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref DATA_BLOB pDataIn, IntPtr ppszDataDescr, ref DATA_BLOB pOptionalEntropy,
        IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DATA_BLOB pDataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    [DllImport("kernel32.dll", EntryPoint = "RtlZeroMemory")]
    private static extern void ZeroMemory(IntPtr dest, UIntPtr size);
}
