using LoginVault.Core.Time;
using LoginVault.Infrastructure.Storage;

namespace LoginVault.Core.Tests.Infrastructure;

/// <summary>A disposable data directory under the test output folder (never the real %LOCALAPPDATA%).</summary>
internal sealed class TempDataDir : IDisposable
{
    public TempDataDir()
    {
        Path = System.IO.Path.Combine(AppContext.BaseDirectory, "testdata", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (IOException)
        {
            // テスト後の後始末失敗は無視する
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>A clock whose time tests advance explicitly.</summary>
internal sealed class FakeClock : IClock
{
    public FakeClock(DateTimeOffset start)
    {
        UtcNow = start;
    }

    public DateTimeOffset UtcNow { get; set; }

    public void Advance(TimeSpan delta) => UtcNow += delta;
}

/// <summary>Wraps the real file system and injects failures on demand. Everything else goes to disk for real.</summary>
internal sealed class FaultyFileSystem : IVaultFileSystem
{
    private readonly IVaultFileSystem _inner = new PhysicalFileSystem();

    public bool FailReplace { get; set; }

    public bool FailWriteNew { get; set; }

    public bool FailMove { get; set; }

    /// <summary>Simulates ReplaceFile failing after the old destination was moved to the backup name (Win32 ERROR_UNABLE_TO_MOVE_REPLACEMENT_2).</summary>
    public bool FailReplaceAfterBackup { get; set; }

    public bool FailCreateDirectory { get; set; }

    /// <summary>Copies the file but flips one byte of the copy (simulates a silently corrupted manual backup).</summary>
    public bool CorruptCopy { get; set; }

    public int ReplaceCalls { get; private set; }

    public bool FileExists(string path) => _inner.FileExists(path);

    public long GetFileLength(string path) => _inner.GetFileLength(path);

    public byte[] ReadAllBytes(string path) => _inner.ReadAllBytes(path);

    public void WriteNewFileDurably(string path, ReadOnlySpan<byte> content)
    {
        if (FailWriteNew)
        {
            throw new IOException("テスト用の書き込み失敗");
        }

        _inner.WriteNewFileDurably(path, content);
    }

    public void ReplaceFile(string sourcePath, string destinationPath, string backupPath)
    {
        ReplaceCalls++;
        if (FailReplace)
        {
            throw new IOException("テスト用の置き換え失敗");
        }

        if (FailReplaceAfterBackup)
        {
            // 置き換え先だけを backup 名へ移した状態で失敗する（置き換え先が消え、新内容は一時ファイルに残る）
            _inner.MoveFile(destinationPath, backupPath);
            throw new IOException("テスト用の置き換え途中失敗");
        }

        _inner.ReplaceFile(sourcePath, destinationPath, backupPath);
    }

    public void MoveFile(string sourcePath, string destinationPath)
    {
        if (FailMove)
        {
            throw new IOException("テスト用の移動失敗");
        }

        _inner.MoveFile(sourcePath, destinationPath);
    }

    public void CopyFile(string sourcePath, string destinationPath, bool overwrite)
    {
        _inner.CopyFile(sourcePath, destinationPath, overwrite);
        if (CorruptCopy)
        {
            var bytes = File.ReadAllBytes(destinationPath);
            bytes[0] ^= 0xFF;
            File.WriteAllBytes(destinationPath, bytes);
        }
    }

    public void DeleteFile(string path) => _inner.DeleteFile(path);

    public void CreateDirectory(string path)
    {
        if (FailCreateDirectory)
        {
            throw new UnauthorizedAccessException("テスト用のディレクトリ作成失敗");
        }

        _inner.CreateDirectory(path);
    }

    public IReadOnlyList<string> ListFiles(string directory, string searchPattern) => _inner.ListFiles(directory, searchPattern);
}

/// <summary>Wraps the real file system and can hold a replace operation open until the test releases it.</summary>
internal sealed class GatedFileSystem : IVaultFileSystem
{
    private readonly IVaultFileSystem _inner = new PhysicalFileSystem();

    public bool BlockNextReplace { get; set; }

    public ManualResetEventSlim ReplaceEntered { get; } = new(false);

    public ManualResetEventSlim ReleaseReplace { get; } = new(false);

    public bool FileExists(string path) => _inner.FileExists(path);

    public long GetFileLength(string path) => _inner.GetFileLength(path);

    public byte[] ReadAllBytes(string path) => _inner.ReadAllBytes(path);

    public void WriteNewFileDurably(string path, ReadOnlySpan<byte> content) => _inner.WriteNewFileDurably(path, content);

    public void ReplaceFile(string sourcePath, string destinationPath, string backupPath)
    {
        if (BlockNextReplace)
        {
            BlockNextReplace = false;
            ReplaceEntered.Set();
            ReleaseReplace.Wait(TimeSpan.FromSeconds(30));
        }

        _inner.ReplaceFile(sourcePath, destinationPath, backupPath);
    }

    public void MoveFile(string sourcePath, string destinationPath) => _inner.MoveFile(sourcePath, destinationPath);

    public void CopyFile(string sourcePath, string destinationPath, bool overwrite) => _inner.CopyFile(sourcePath, destinationPath, overwrite);

    public void DeleteFile(string path) => _inner.DeleteFile(path);

    public void CreateDirectory(string path) => _inner.CreateDirectory(path);

    public IReadOnlyList<string> ListFiles(string directory, string searchPattern) => _inner.ListFiles(directory, searchPattern);
}

internal static class Secrets
{
    // すべて架空の値。平文残留の検査に使う
    public const string MasterPassword = "テスト用マスター pass 🔐";
    public const string OtherPassword = "別のマスター 123 ";
    public const string AccountPassword = "sentinel-PW-架空-9f3a";
    public const string LoginId = "sentinel.login@example.test";
    public const string Notes = "sentinel-NOTE-架空-7c1d";
    public const string ServiceName = "SentinelService架空";

    public static IEnumerable<string> All => new[] { AccountPassword, LoginId, Notes, ServiceName };

    /// <summary>Returns the files under <paramref name="directory"/> that contain the text in UTF-8, UTF-16LE or JSON-escaped (\uXXXX) form.</summary>
    public static IReadOnlyList<string> FilesContaining(string directory, string text)
    {
        var needles = new[]
        {
            System.Text.Encoding.UTF8.GetBytes(text),
            System.Text.Encoding.Unicode.GetBytes(text),
            // 既定の JSON エンコーダーは非 ASCII を \uXXXX にするため、その形でも探す
            System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonEncodedText.Encode(text).ToString()),
        };
        var hits = new List<string>();
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            var bytes = File.ReadAllBytes(file);
            if (needles.Any(n => bytes.AsSpan().IndexOf(n) >= 0))
            {
                hits.Add(file);
            }
        }

        return hits;
    }
}
