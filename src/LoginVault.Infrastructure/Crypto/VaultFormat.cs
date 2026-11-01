namespace LoginVault.Infrastructure.Crypto;

/// <summary>
/// Constants of the vault file format (version 1). Layout, little-endian:
/// <code>
/// offset size  field
/// 0      4     magic "LVLT"
/// 4      2     format version (1)
/// 6      1     KDF id (1 = PBKDF2-HMAC-SHA256)
/// 7      1     cipher id (1 = AES-256-GCM)
/// 8      4     KDF iterations
/// 12     2     salt length
/// 14     2     nonce length (12)
/// 16     2     tag length (16)
/// 18     4     ciphertext length
/// 22     n     salt
/// 22+n   12    nonce
/// ...    m     ciphertext
/// ...    16    authentication tag
/// </code>
/// Everything before the ciphertext is the associated data of AES-GCM, so any change to the header fails authentication.
/// </summary>
public static class VaultFormat
{
    public static ReadOnlySpan<byte> Magic => "LVLT"u8;

    public const ushort Version = 1;
    public const byte KdfPbkdf2HmacSha256 = 1;
    public const byte CipherAes256Gcm = 1;

    public const int FixedHeaderLength = 22;

    /// <summary>OWASP's current PBKDF2-HMAC-SHA256 recommendation is the floor; the default equals it.</summary>
    public const int MinIterations = 600_000;
    public const int MaxIterations = 10_000_000;
    public const int DefaultIterations = 600_000;

    public const int MinSaltLength = 16;
    public const int MaxSaltLength = 64;
    public const int DefaultSaltLength = 32;

    public const int NonceLength = 12;
    public const int TagLength = 16;
    public const int KeyLength = 32;

    public const int MinCiphertextLength = 1;
    public const int MaxCiphertextLength = 64 * 1024 * 1024;

    public const long MinFileLength = FixedHeaderLength + MinSaltLength + NonceLength + MinCiphertextLength + TagLength;
    public const long MaxFileLength = FixedHeaderLength + MaxSaltLength + NonceLength + MaxCiphertextLength + TagLength;
}

/// <summary>Why a file was rejected before or during header parsing.</summary>
public enum VaultFormatError
{
    FileTooSmall,
    FileTooLarge,
    UnknownFormat,
    UnsupportedVersion,
    UnsupportedAlgorithm,
    InvalidKdfParameters,
    InvalidLength,
    LengthMismatch,
}

/// <summary>The file is not a vault this version can read (wrong magic, version, lengths or KDF limits).</summary>
public sealed class VaultFormatException : Exception
{
    public VaultFormatException(VaultFormatError error, string message)
        : base(message)
    {
        Error = error;
    }

    public VaultFormatError Error { get; }
}

/// <summary>Decryption failed: wrong master password, or the file was modified or truncated. The two cases are indistinguishable by design.</summary>
public sealed class VaultAuthenticationException : Exception
{
    public const string DefaultMessage = "マスターパスワードが違うか、保管庫ファイルが破損または改ざんされています。";

    public VaultAuthenticationException()
        : base(DefaultMessage)
    {
    }

    public VaultAuthenticationException(Exception innerException)
        : base(DefaultMessage, innerException)
    {
    }
}

/// <summary>The parsed, validated plaintext header of a vault file.</summary>
public sealed record VaultHeader(
    ushort Version,
    byte KdfId,
    byte CipherId,
    int Iterations,
    byte[] Salt,
    byte[] Nonce,
    int CiphertextLength,
    int HeaderLength);
