using System.Buffers.Binary;
using System.Security.Cryptography;

namespace LoginVault.Infrastructure.Crypto;

/// <summary>Encrypts and decrypts vault files (see <see cref="VaultFormat"/>). Header parsing is strict and happens before any key derivation.</summary>
public static class VaultCipher
{
    /// <summary>Encrypts the plaintext with a fresh random nonce; the whole header is authenticated as associated data.</summary>
    public static byte[] Encrypt(VaultKey key, ReadOnlySpan<byte> plaintext)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (plaintext.Length < VaultFormat.MinCiphertextLength || plaintext.Length > VaultFormat.MaxCiphertextLength)
        {
            throw new ArgumentOutOfRangeException(nameof(plaintext), "暗号化する本文の長さが許容範囲外です。");
        }

        var keyBytes = key.Bytes;
        key.Kdf.EnsureValid();
        // nonce は保存のたびに新しい乱数を生成し、同一鍵で再使用しない
        var nonce = RandomNumberGenerator.GetBytes(VaultFormat.NonceLength);
        var header = BuildHeader(key.Kdf, nonce, plaintext.Length);

        var output = new byte[header.Length + plaintext.Length + VaultFormat.TagLength];
        header.CopyTo(output, 0);
        using var aes = new AesGcm(keyBytes, VaultFormat.TagLength);
        aes.Encrypt(
            nonce,
            plaintext,
            output.AsSpan(header.Length, plaintext.Length),
            output.AsSpan(header.Length + plaintext.Length, VaultFormat.TagLength),
            header);
        return output;
    }

    /// <summary>Decrypts with an already derived key. The key must have been derived with the salt and iterations recorded in the file.</summary>
    public static byte[] Decrypt(VaultKey key, ReadOnlySpan<byte> file)
    {
        ArgumentNullException.ThrowIfNull(key);
        var header = ParseHeader(file);
        var keyBytes = key.Bytes;
        if (header.Iterations != key.Kdf.Iterations || !header.Salt.AsSpan().SequenceEqual(key.Kdf.Salt))
        {
            // 別の salt／反復回数で導出した鍵では復号できない。パスワード誤りと同じ扱いにする
            throw new VaultAuthenticationException();
        }

        return DecryptCore(keyBytes, header, file);
    }

    /// <summary>Derives the key from the master password using the header parameters, then decrypts. On success the key is returned for later saves.</summary>
    public static byte[] DecryptWithPassword(string masterPassword, ReadOnlySpan<byte> file, out VaultKey key)
    {
        ArgumentNullException.ThrowIfNull(masterPassword);
        // 鍵導出（高コスト）の前にヘッダーと長さの上下限を検査する
        var header = ParseHeader(file);
        var derived = VaultKey.Derive(masterPassword, new KdfParameters(header.Iterations, header.Salt));
        try
        {
            var plaintext = DecryptCore(derived.Bytes, header, file);
            key = derived;
            return plaintext;
        }
        catch
        {
            derived.Dispose();
            throw;
        }
    }

    /// <summary>Parses and validates the plaintext header. Throws <see cref="VaultFormatException"/> for anything out of specification.</summary>
    public static VaultHeader ParseHeader(ReadOnlySpan<byte> file)
    {
        if (file.Length < VaultFormat.MinFileLength)
        {
            throw new VaultFormatException(VaultFormatError.FileTooSmall, "ファイルが小さすぎるため保管庫として読み込めません。");
        }

        if (file.Length > VaultFormat.MaxFileLength)
        {
            throw new VaultFormatException(VaultFormatError.FileTooLarge, "ファイルが大きすぎるため保管庫として読み込めません。");
        }

        if (!file[..4].SequenceEqual(VaultFormat.Magic))
        {
            throw new VaultFormatException(VaultFormatError.UnknownFormat, "Login Vault の保管庫ファイルではありません。");
        }

        var version = BinaryPrimitives.ReadUInt16LittleEndian(file.Slice(4, 2));
        if (version != VaultFormat.Version)
        {
            throw new VaultFormatException(VaultFormatError.UnsupportedVersion, $"未対応のファイル形式バージョンです（{version}）。");
        }

        var kdfId = file[6];
        var cipherId = file[7];
        if (kdfId != VaultFormat.KdfPbkdf2HmacSha256 || cipherId != VaultFormat.CipherAes256Gcm)
        {
            throw new VaultFormatException(VaultFormatError.UnsupportedAlgorithm, "未対応の鍵導出方式または暗号方式です。");
        }

        var iterations = BinaryPrimitives.ReadUInt32LittleEndian(file.Slice(8, 4));
        if (iterations < VaultFormat.MinIterations || iterations > VaultFormat.MaxIterations)
        {
            throw new VaultFormatException(VaultFormatError.InvalidKdfParameters, "鍵導出の反復回数が許容範囲外です。");
        }

        int saltLength = BinaryPrimitives.ReadUInt16LittleEndian(file.Slice(12, 2));
        int nonceLength = BinaryPrimitives.ReadUInt16LittleEndian(file.Slice(14, 2));
        int tagLength = BinaryPrimitives.ReadUInt16LittleEndian(file.Slice(16, 2));
        if (saltLength < VaultFormat.MinSaltLength || saltLength > VaultFormat.MaxSaltLength
            || nonceLength != VaultFormat.NonceLength || tagLength != VaultFormat.TagLength)
        {
            throw new VaultFormatException(VaultFormatError.InvalidLength, "ヘッダーの長さ情報が不正です。");
        }

        var ciphertextLength = BinaryPrimitives.ReadUInt32LittleEndian(file.Slice(18, 4));
        if (ciphertextLength < VaultFormat.MinCiphertextLength || ciphertextLength > VaultFormat.MaxCiphertextLength)
        {
            throw new VaultFormatException(VaultFormatError.InvalidLength, "本文の長さ情報が不正です。");
        }

        var headerLength = VaultFormat.FixedHeaderLength + saltLength + nonceLength;
        var expectedLength = (long)headerLength + ciphertextLength + tagLength;
        if (file.Length != expectedLength)
        {
            throw new VaultFormatException(VaultFormatError.LengthMismatch, "ファイルの長さがヘッダーの内容と一致しません（切り詰めまたは破損）。");
        }

        var salt = file.Slice(VaultFormat.FixedHeaderLength, saltLength).ToArray();
        var nonce = file.Slice(VaultFormat.FixedHeaderLength + saltLength, nonceLength).ToArray();
        return new VaultHeader(version, kdfId, cipherId, (int)iterations, salt, nonce, (int)ciphertextLength, headerLength);
    }

    private static byte[] BuildHeader(KdfParameters kdf, byte[] nonce, int ciphertextLength)
    {
        var header = new byte[VaultFormat.FixedHeaderLength + kdf.Salt.Length + nonce.Length];
        var span = header.AsSpan();
        VaultFormat.Magic.CopyTo(span);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(4, 2), VaultFormat.Version);
        span[6] = VaultFormat.KdfPbkdf2HmacSha256;
        span[7] = VaultFormat.CipherAes256Gcm;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(8, 4), (uint)kdf.Iterations);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(12, 2), (ushort)kdf.Salt.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(14, 2), (ushort)nonce.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(16, 2), VaultFormat.TagLength);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(18, 4), (uint)ciphertextLength);
        kdf.Salt.CopyTo(span[VaultFormat.FixedHeaderLength..]);
        nonce.CopyTo(span[(VaultFormat.FixedHeaderLength + kdf.Salt.Length)..]);
        return header;
    }

    private static byte[] DecryptCore(ReadOnlySpan<byte> keyBytes, VaultHeader header, ReadOnlySpan<byte> file)
    {
        var associatedData = file[..header.HeaderLength];
        var ciphertext = file.Slice(header.HeaderLength, header.CiphertextLength);
        var tag = file.Slice(header.HeaderLength + header.CiphertextLength, VaultFormat.TagLength);
        var plaintext = new byte[header.CiphertextLength];
        try
        {
            using var aes = new AesGcm(keyBytes, VaultFormat.TagLength);
            aes.Decrypt(header.Nonce, ciphertext, tag, plaintext, associatedData);
            return plaintext;
        }
        catch (CryptographicException ex)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw new VaultAuthenticationException(ex);
        }
    }
}
