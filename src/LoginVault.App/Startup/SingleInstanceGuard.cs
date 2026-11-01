using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace LoginVault.App.Startup;

/// <summary>
/// Prevents two instances from opening the same data directory (and overwriting each other's saves).
/// The mutex name is derived from a hash of the directory so different data directories do not block each other.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex _mutex;

    public SingleInstanceGuard(string dataDirectory)
    {
        _mutex = new Mutex(initiallyOwned: true, MutexNameFor(dataDirectory), out var createdNew);
        IsPrimaryInstance = createdNew;
    }

    public bool IsPrimaryInstance { get; }

    public static string MutexNameFor(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        // Windows のパスは大文字小文字を区別しないため正規化してからハッシュする
        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataDirectory)).ToUpperInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return "Local\\LoginVault." + Convert.ToHexString(hash.AsSpan(0, 16));
    }

    public void Dispose()
    {
        if (IsPrimaryInstance)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // 所有していないスレッドからの解放は無視する
            }
        }

        _mutex.Dispose();
    }
}
