using System.Security.Cryptography;

namespace LoginVault.Infrastructure.Crypto;

/// <summary>Key derivation settings stored in the file header so the vault can be reopened anywhere.</summary>
public sealed record KdfParameters(int Iterations, byte[] Salt)
{
    /// <summary>Production defaults: 600,000 iterations and a fresh 32-byte random salt.</summary>
    public static KdfParameters CreateDefault() =>
        new(VaultFormat.DefaultIterations, RandomNumberGenerator.GetBytes(VaultFormat.DefaultSaltLength));

    public bool IsValid =>
        Iterations >= VaultFormat.MinIterations
        && Iterations <= VaultFormat.MaxIterations
        && Salt is not null
        && Salt.Length >= VaultFormat.MinSaltLength
        && Salt.Length <= VaultFormat.MaxSaltLength;

    public void EnsureValid()
    {
        if (!IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(Iterations), "鍵導出パラメーターが許容範囲外です。");
        }
    }

    public override string ToString() => $"KdfParameters(iterations={Iterations}, saltLength={Salt?.Length ?? 0})";
}
