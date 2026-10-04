using System;
using System.IO;
using System.Security.Cryptography;
using ClipboardManager.Core.Platform;

namespace ClipboardManager.Mac.Platform;

/// <summary>
/// macOS local data protector using AES-256-GCM.
/// The encryption key is derived and saved in a private key file with restricted permissions (0600)
/// in the user's Application Support folder.
/// </summary>
public sealed class MacDataProtector : IDataProtector
{
    private const int KeySizeBytes = 32;
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;
    private readonly byte[] _masterKey;

    public MacDataProtector(string dataFolder)
    {
        Directory.CreateDirectory(dataFolder);
        var keyPath = Path.Combine(dataFolder, ".master.key");

        if (File.Exists(keyPath))
        {
            var raw = File.ReadAllBytes(keyPath);
            if (raw.Length == KeySizeBytes)
            {
                _masterKey = raw;
                return;
            }
        }

        // Generate new key
        _masterKey = new byte[KeySizeBytes];
        RandomNumberGenerator.Fill(_masterKey);
        File.WriteAllBytes(keyPath, _masterKey);

        if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
        {
            try
            {
                File.SetUnixFileMode(keyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch
            {
                // Ignore permission error if filesystem doesn't support Unix permissions
            }
        }
    }

    public byte[] Protect(byte[] plain)
    {
        var nonce = new byte[NonceSizeBytes];
        RandomNumberGenerator.Fill(nonce);

        var tag = new byte[TagSizeBytes];
        var cipher = new byte[plain.Length];

        using var aes = new AesGcm(_masterKey, TagSizeBytes);
        aes.Encrypt(nonce, plain, cipher, tag);

        var result = new byte[NonceSizeBytes + TagSizeBytes + plain.Length];
        Buffer.BlockCopy(nonce, 0, result, 0, NonceSizeBytes);
        Buffer.BlockCopy(tag, 0, result, NonceSizeBytes, TagSizeBytes);
        Buffer.BlockCopy(cipher, 0, result, NonceSizeBytes + TagSizeBytes, plain.Length);
        return result;
    }

    public byte[] Unprotect(byte[] protectedData)
    {
        if (protectedData.Length < NonceSizeBytes + TagSizeBytes)
        {
            throw new CryptographicException("Ciphertext too short.");
        }

        var nonce = protectedData[..NonceSizeBytes];
        var tag = protectedData[NonceSizeBytes..(NonceSizeBytes + TagSizeBytes)];
        var cipher = protectedData[(NonceSizeBytes + TagSizeBytes)..];

        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(_masterKey, TagSizeBytes);
        aes.Decrypt(nonce, cipher, tag, plain);
        return plain;
    }
}
