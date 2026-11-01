using System.Security.Cryptography;
using System.Text;

namespace LoginVault.Infrastructure.Crypto;

/// <summary>A derived AES-256 key. Disposing zeroes the key material.</summary>
public sealed class VaultKey : IDisposable
{
    private readonly byte[] _key;
    private bool _disposed;

    private VaultKey(byte[] key, KdfParameters kdf)
    {
        _key = key;
        Kdf = kdf;
    }

    public KdfParameters Kdf { get; }

    /// <summary>Key bytes for the cipher. Throws once disposed.</summary>
    internal ReadOnlySpan<byte> Bytes
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _key;
        }
    }

    /// <summary>
    /// Derives the key with PBKDF2-HMAC-SHA256 using the static <see cref="Rfc2898DeriveBytes.Pbkdf2(byte[], byte[], int, HashAlgorithmName, int)"/>.
    /// The password is used exactly as given (no trimming or normalization) and its UTF-8 bytes are wiped afterwards.
    /// </summary>
    public static VaultKey Derive(string masterPassword, KdfParameters kdf)
    {
        ArgumentNullException.ThrowIfNull(masterPassword);
        ArgumentNullException.ThrowIfNull(kdf);
        kdf.EnsureValid();

        var passwordBytes = Encoding.UTF8.GetBytes(masterPassword);
        try
        {
            var key = Rfc2898DeriveBytes.Pbkdf2(passwordBytes, kdf.Salt, kdf.Iterations, HashAlgorithmName.SHA256, VaultFormat.KeyLength);
            return new VaultKey(key, kdf);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        CryptographicOperations.ZeroMemory(_key);
        _disposed = true;
    }

    public override string ToString() => "VaultKey";
}
