namespace ClipboardManager.Core.Platform;

/// <summary>
/// Encrypts data for the current OS user (Windows: DPAPI, CurrentUser scope). Used for database encryption:
/// only the same Windows account on the same machine can decrypt, and no password is needed.
/// </summary>
public interface IDataProtector
{
    byte[] Protect(byte[] plain);

    /// <exception cref="System.Security.Cryptography.CryptographicException">Data was protected for another user/machine, or is corrupt.</exception>
    byte[] Unprotect(byte[] protectedData);
}
