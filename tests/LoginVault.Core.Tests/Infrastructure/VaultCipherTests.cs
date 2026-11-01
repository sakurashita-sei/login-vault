using System.Buffers.Binary;
using System.Text;
using LoginVault.Infrastructure.Crypto;

namespace LoginVault.Core.Tests.Infrastructure;

/// <summary>Derives one real key (production KDF settings) and shares it across the class.</summary>
public sealed class KeyFixture : IDisposable
{
    public KeyFixture()
    {
        Kdf = KdfParameters.CreateDefault();
        Key = VaultKey.Derive(Secrets.MasterPassword, Kdf);
    }

    public KdfParameters Kdf { get; }

    public VaultKey Key { get; }

    public void Dispose() => Key.Dispose();
}

public class VaultCipherTests : IClassFixture<KeyFixture>
{
    private static readonly byte[] Plaintext = Encoding.UTF8.GetBytes("{\"secret\":\"架空の本文 " + Secrets.AccountPassword + "\"}");
    private readonly KeyFixture _fx;

    public VaultCipherTests(KeyFixture fx)
    {
        _fx = fx;
    }

    [Fact]
    public void DefaultParameters_MeetMinimums()
    {
        Assert.True(_fx.Kdf.Iterations >= 600_000);
        Assert.True(_fx.Kdf.Salt.Length >= 16);
        Assert.Equal(600_000, VaultFormat.MinIterations);
        Assert.Equal(12, VaultFormat.NonceLength);
        Assert.Equal(16, VaultFormat.TagLength);
        Assert.Equal(32, VaultFormat.KeyLength);
        Assert.Equal(600_000, VaultFormat.DefaultIterations);
        Assert.Equal(32, VaultFormat.DefaultSaltLength);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(7)]
    public void Decrypt_UnknownKdfOrCipherKind_ThrowsFormatException(int offset)
    {
        var file = VaultCipher.Encrypt(_fx.Key, Plaintext);
        file[offset] = 2;

        var ex = Assert.Throws<VaultFormatException>(() => VaultCipher.Decrypt(_fx.Key, file));
        Assert.Equal(VaultFormatError.UnsupportedAlgorithm, ex.Error);
    }

    [Fact]
    public void EncryptThenDecrypt_WithSameKey_RoundTrips()
    {
        var file = VaultCipher.Encrypt(_fx.Key, Plaintext);
        var decrypted = VaultCipher.Decrypt(_fx.Key, file);

        Assert.Equal(Plaintext, decrypted);
    }

    [Fact]
    public void Encrypt_HeaderDescribesContent()
    {
        var file = VaultCipher.Encrypt(_fx.Key, Plaintext);
        var header = VaultCipher.ParseHeader(file);

        Assert.Equal(VaultFormat.Version, header.Version);
        Assert.Equal(VaultFormat.KdfPbkdf2HmacSha256, header.KdfId);
        Assert.Equal(VaultFormat.CipherAes256Gcm, header.CipherId);
        Assert.Equal(_fx.Kdf.Iterations, header.Iterations);
        Assert.Equal(_fx.Kdf.Salt, header.Salt);
        Assert.Equal(Plaintext.Length, header.CiphertextLength);
        Assert.Equal(header.HeaderLength + header.CiphertextLength + VaultFormat.TagLength, file.Length);
    }

    [Fact]
    public void Encrypt_DoesNotContainPlaintext()
    {
        var file = VaultCipher.Encrypt(_fx.Key, Plaintext);

        Assert.True(file.AsSpan().IndexOf(Encoding.UTF8.GetBytes(Secrets.AccountPassword)) < 0);
    }

    [Fact]
    public void Encrypt_SameInputTwice_ProducesDifferentNonceAndCiphertext()
    {
        var first = VaultCipher.Encrypt(_fx.Key, Plaintext);
        var second = VaultCipher.Encrypt(_fx.Key, Plaintext);

        Assert.NotEqual(first, second);
        Assert.NotEqual(VaultCipher.ParseHeader(first).Nonce, VaultCipher.ParseHeader(second).Nonce);
    }

    [Fact]
    public void DecryptWithPassword_RoundTrips_AndReturnsReusableKey()
    {
        var file = VaultCipher.Encrypt(_fx.Key, Plaintext);

        var decrypted = VaultCipher.DecryptWithPassword(Secrets.MasterPassword, file, out var key);
        using (key)
        {
            Assert.Equal(Plaintext, decrypted);
            Assert.Equal(Plaintext, VaultCipher.Decrypt(key, VaultCipher.Encrypt(key, Plaintext)));
        }
    }

    [Theory]
    [InlineData(Secrets.OtherPassword)]
    [InlineData("テスト用マスター pass 🔐 ")]
    [InlineData("テスト用マスター pass")]
    public void DecryptWithPassword_WrongOrTrimmedPassword_Throws(string password)
    {
        var file = VaultCipher.Encrypt(_fx.Key, Plaintext);

        Assert.Throws<VaultAuthenticationException>(() => VaultCipher.DecryptWithPassword(password, file, out _));
    }

    [Fact]
    public void Decrypt_TamperedCiphertextByte_Throws()
    {
        var file = VaultCipher.Encrypt(_fx.Key, Plaintext);
        var header = VaultCipher.ParseHeader(file);
        file[header.HeaderLength + 3] ^= 0x01;

        Assert.Throws<VaultAuthenticationException>(() => VaultCipher.Decrypt(_fx.Key, file));
    }

    [Fact]
    public void Decrypt_TamperedTag_Throws()
    {
        var file = VaultCipher.Encrypt(_fx.Key, Plaintext);
        file[^1] ^= 0x80;

        Assert.Throws<VaultAuthenticationException>(() => VaultCipher.Decrypt(_fx.Key, file));
    }

    [Fact]
    public void Decrypt_TamperedIterationsInHeader_Throws()
    {
        var file = VaultCipher.Encrypt(_fx.Key, Plaintext);
        // 反復回数を有効範囲内の別の値へ書き換える（ヘッダーは認証対象）
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(8, 4), (uint)(_fx.Kdf.Iterations + 1));

        Assert.Throws<VaultAuthenticationException>(() => VaultCipher.Decrypt(_fx.Key, file));
        Assert.Throws<VaultAuthenticationException>(() => VaultCipher.DecryptWithPassword(Secrets.MasterPassword, file, out _));
    }

    [Fact]
    public void Decrypt_TamperedSaltByte_Throws()
    {
        var file = VaultCipher.Encrypt(_fx.Key, Plaintext);
        file[VaultFormat.FixedHeaderLength] ^= 0x01;

        Assert.Throws<VaultAuthenticationException>(() => VaultCipher.Decrypt(_fx.Key, file));
    }

    [Fact]
    public void Decrypt_TamperedNonceByte_Throws()
    {
        var file = VaultCipher.Encrypt(_fx.Key, Plaintext);
        file[VaultFormat.FixedHeaderLength + _fx.Kdf.Salt.Length] ^= 0x01;

        Assert.Throws<VaultAuthenticationException>(() => VaultCipher.Decrypt(_fx.Key, file));
    }

    [Fact]
    public void Decrypt_TruncatedFile_ThrowsFormatException()
    {
        var file = VaultCipher.Encrypt(_fx.Key, Plaintext);
        var truncated = file[..^5];

        var ex = Assert.Throws<VaultFormatException>(() => VaultCipher.Decrypt(_fx.Key, truncated));
        Assert.Equal(VaultFormatError.LengthMismatch, ex.Error);
    }

    [Fact]
    public void Decrypt_FileSmallerThanMinimum_ThrowsFormatException()
    {
        var ex = Assert.Throws<VaultFormatException>(() => VaultCipher.Decrypt(_fx.Key, new byte[10]));

        Assert.Equal(VaultFormatError.FileTooSmall, ex.Error);
    }

    [Fact]
    public void Decrypt_UnknownMagic_ThrowsFormatException()
    {
        var file = VaultCipher.Encrypt(_fx.Key, Plaintext);
        file[0] = (byte)'X';

        var ex = Assert.Throws<VaultFormatException>(() => VaultCipher.Decrypt(_fx.Key, file));
        Assert.Equal(VaultFormatError.UnknownFormat, ex.Error);
    }

    [Fact]
    public void Decrypt_UnsupportedVersion_ThrowsFormatException()
    {
        var file = VaultCipher.Encrypt(_fx.Key, Plaintext);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(4, 2), 2);

        var ex = Assert.Throws<VaultFormatException>(() => VaultCipher.Decrypt(_fx.Key, file));
        Assert.Equal(VaultFormatError.UnsupportedVersion, ex.Error);
    }

    [Theory]
    [InlineData(1_000u)]
    [InlineData(599_999u)]
    [InlineData(10_000_001u)]
    [InlineData(uint.MaxValue)]
    public void Decrypt_IterationsOutOfRange_RejectedBeforeKeyDerivation(uint iterations)
    {
        var file = VaultCipher.Encrypt(_fx.Key, Plaintext);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(8, 4), iterations);

        var started = DateTime.UtcNow;
        var ex = Assert.Throws<VaultFormatException>(() => VaultCipher.DecryptWithPassword(Secrets.MasterPassword, file, out _));

        Assert.Equal(VaultFormatError.InvalidKdfParameters, ex.Error);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(2), "過大な反復回数が鍵導出前に拒否されていません。");
    }

    [Theory]
    [InlineData(12, 8)]
    [InlineData(12, 65)]
    [InlineData(14, 16)]
    [InlineData(16, 12)]
    public void Decrypt_InvalidSaltOrNonceLength_ThrowsFormatException(int fieldOffset, int value)
    {
        var file = VaultCipher.Encrypt(_fx.Key, Plaintext);
        // 12: salt 長、14: nonce 長、16: タグ長 のフィールドを不正値に書き換える
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(fieldOffset, 2), (ushort)value);

        var ex = Assert.Throws<VaultFormatException>(() => VaultCipher.Decrypt(_fx.Key, file));
        Assert.Equal(VaultFormatError.InvalidLength, ex.Error);
    }

    [Fact]
    public void Decrypt_CiphertextLengthFieldTooLarge_RejectedBeforeAllocation()
    {
        var file = VaultCipher.Encrypt(_fx.Key, Plaintext);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(18, 4), uint.MaxValue);

        var ex = Assert.Throws<VaultFormatException>(() => VaultCipher.Decrypt(_fx.Key, file));
        Assert.Equal(VaultFormatError.InvalidLength, ex.Error);
    }

    [Fact]
    public void Decrypt_CiphertextLengthFieldMismatch_ThrowsFormatException()
    {
        var file = VaultCipher.Encrypt(_fx.Key, Plaintext);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(18, 4), (uint)(Plaintext.Length + 1));

        var ex = Assert.Throws<VaultFormatException>(() => VaultCipher.Decrypt(_fx.Key, file));
        Assert.Equal(VaultFormatError.LengthMismatch, ex.Error);
    }

    [Fact]
    public void Decrypt_WithKeyOfDifferentSalt_Throws()
    {
        var file = VaultCipher.Encrypt(_fx.Key, Plaintext);
        using var otherKey = VaultKey.Derive(Secrets.MasterPassword, KdfParameters.CreateDefault());

        Assert.Throws<VaultAuthenticationException>(() => VaultCipher.Decrypt(otherKey, file));
    }

    [Fact]
    public void VaultKey_AfterDispose_CannotBeUsedForEncryption()
    {
        var key = VaultKey.Derive(Secrets.MasterPassword, _fx.Kdf);
        key.Dispose();

        Assert.Throws<ObjectDisposedException>(() => VaultCipher.Encrypt(key, Plaintext));
    }
}
