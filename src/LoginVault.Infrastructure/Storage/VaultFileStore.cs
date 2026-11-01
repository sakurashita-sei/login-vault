using LoginVault.Core.Time;
using LoginVault.Infrastructure.Crypto;

namespace LoginVault.Infrastructure.Storage;

public enum VaultStorageError
{
    NotFound,
    AlreadyExists,
    ReadFailed,
    WriteFailed,
    ReplaceFailed,
    CopyFailed,

    /// <summary>No vault file, but backups or an interrupted save are present; a new vault is not created over them.</summary>
    RecoverableFilesPresent,

    /// <summary>A manual backup destination inside the data directory (would be mistaken for a generation or temp file).</summary>
    InvalidDestination,
}

public sealed class VaultStorageException : Exception
{
    public VaultStorageException(VaultStorageError error, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Error = error;
    }

    public VaultStorageError Error { get; }
}

/// <summary>
/// Reads and writes the encrypted vault file. Writes go to an encrypted temporary file in the same
/// directory and then replace the vault, keeping the previous version as a generation backup. The
/// replacement (Win32 ReplaceFile) can fail halfway, leaving no vault file; <see cref="Replace"/> then
/// promotes the new content or restores the old one before reporting. The store never sees plaintext.
/// </summary>
public sealed class VaultFileStore
{
    private readonly IVaultFileSystem _fs;
    private readonly IClock _clock;
    private int _generationSequence;

    public VaultFileStore(IVaultFileSystem fileSystem, VaultPaths paths, IClock clock)
    {
        _fs = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        Paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public VaultPaths Paths { get; }

    public bool Exists => _fs.FileExists(Paths.VaultFile);

    /// <summary>
    /// True when generation backups (including stray vault-*.dat copies that are not generations), pre-restore copies
    /// or an interrupted save are present (relevant when the vault file itself is missing).
    /// </summary>
    public bool HasRecoverableFiles =>
        _fs.ListFiles(Paths.BackupDirectory, VaultPaths.GenerationSearchPattern).Count > 0
        || ListPreRestoreFiles().Count > 0
        || ListStaleTempFiles().Count > 0;

    /// <summary>Reads the vault file. The size limit is checked before reading.</summary>
    public byte[] Read() => ReadExternal(Paths.VaultFile);

    /// <summary>Reads any vault file (e.g. a backup chosen by the user) with the same size limit.</summary>
    public byte[] ReadExternal(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!_fs.FileExists(path))
        {
            throw new VaultStorageException(VaultStorageError.NotFound, "保管庫ファイルが見つかりません。");
        }

        try
        {
            if (_fs.GetFileLength(path) > VaultFormat.MaxFileLength)
            {
                throw new VaultFormatException(VaultFormatError.FileTooLarge, "ファイルが大きすぎるため保管庫として読み込めません。");
            }

            return _fs.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new VaultStorageException(VaultStorageError.ReadFailed, "保管庫ファイルを読み込めませんでした。", ex);
        }
    }

    /// <summary>First-time creation. Fails if a vault already exists; it is never overwritten.</summary>
    public void WriteNew(byte[] encrypted)
    {
        ArgumentNullException.ThrowIfNull(encrypted);
        if (Exists)
        {
            throw new VaultStorageException(VaultStorageError.AlreadyExists, "保管庫ファイルは既に存在します。");
        }

        EnsureDirectoriesForWrite();
        var temp = Paths.NewTempFile();
        WriteTemp(temp, encrypted);
        try
        {
            _fs.MoveFile(temp, Paths.VaultFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(temp);
            throw new VaultStorageException(VaultStorageError.WriteFailed, "保管庫ファイルを作成できませんでした。", ex);
        }
    }

    /// <summary>Replaces the existing vault atomically, keeping the previous version as a generation, then prunes old generations.</summary>
    public void Replace(byte[] encrypted, int generationsToKeep)
    {
        ArgumentNullException.ThrowIfNull(encrypted);
        ArgumentOutOfRangeException.ThrowIfLessThan(generationsToKeep, 1);
        if (!Exists)
        {
            throw new VaultStorageException(VaultStorageError.NotFound, "置き換える保管庫ファイルが存在しません。");
        }

        EnsureDirectoriesForWrite();
        // 世代名は一時ファイルを書く前に決める（決められない場合に何も残さない）
        var generation = NewGenerationFile();
        var temp = Paths.NewTempFile();
        WriteTemp(temp, encrypted);
        try
        {
            _fs.ReplaceFile(temp, Paths.VaultFile, generation);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (_fs.FileExists(Paths.VaultFile))
            {
                // 何も置き換わっていない失敗: 一時ファイルを消して報告する
                TryDelete(temp);
                throw new VaultStorageException(VaultStorageError.ReplaceFailed, "保管庫ファイルの置き換えに失敗しました。現在の保管庫は変更されていません。", ex);
            }

            // ReplaceFile は旧版を世代名へ移した後で失敗することがある（vault.dat が存在しない状態）。
            // まず新しい内容を昇格させ、だめなら旧版を戻す。どちらもできなければ両方のファイルを残して伝える
            if (TryMove(temp, Paths.VaultFile))
            {
                PruneGenerations(generationsToKeep);
                return;
            }

            if (_fs.FileExists(generation) && TryMove(generation, Paths.VaultFile))
            {
                TryDelete(temp);
                throw new VaultStorageException(VaultStorageError.ReplaceFailed, "保管庫ファイルの置き換えに失敗しました。現在の保管庫は変更されていません。", ex);
            }

            throw new VaultStorageException(
                VaultStorageError.ReplaceFailed,
                "保管庫ファイルの置き換えが途中で失敗し、保管庫ファイル（vault.dat）がない状態です。直前の内容は backups フォルダーの最新の vault-*.dat に、保存しようとした内容は一時ファイル（vault.dat.*.tmp）に残っています。アプリを終了し、どちらかを vault.dat という名前に戻してください。",
                ex);
        }

        // 世代整理は保存成功後に行い、失敗しても保存結果には影響させない
        PruneGenerations(generationsToKeep);
    }

    /// <summary>Removes encrypted temp files left behind by a crash. Safe because a single instance owns the directory.</summary>
    public void DeleteStaleTempFiles()
    {
        try
        {
            foreach (var temp in _fs.ListFiles(Paths.DataDirectory, VaultPaths.TempSearchPattern))
            {
                _fs.DeleteFile(temp);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 掃除の失敗は致命的ではない（暗号化済み）
        }
    }

    /// <summary>Generation backups, newest first. Only names this app produces count; other vault-*.dat files in the folder are left alone.</summary>
    public IReadOnlyList<string> ListGenerationFiles() =>
        _fs.ListFiles(Paths.BackupDirectory, VaultPaths.GenerationSearchPattern)
            // 世代名の形式でないもの（利用者の手動コピーなど）は世代として数えない: 整理で消さず、採番の基準にもしない
            .Where(path => VaultPaths.TryParseGenerationStamp(path, out _))
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
            .ToList();

    /// <summary>Copies saved before a restore (never pruned).</summary>
    public IReadOnlyList<string> ListPreRestoreFiles() =>
        _fs.ListFiles(Paths.BackupDirectory, VaultPaths.PreRestoreSearchPattern);

    /// <summary>Encrypted temp files of interrupted saves next to the vault.</summary>
    public IReadOnlyList<string> ListStaleTempFiles() =>
        _fs.ListFiles(Paths.DataDirectory, VaultPaths.TempSearchPattern);

    /// <summary>Copies the encrypted vault to a user-chosen location (manual backup).</summary>
    public string CopyTo(string destinationPath, bool overwrite)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        if (!Exists)
        {
            throw new VaultStorageException(VaultStorageError.NotFound, "保管庫ファイルが見つかりません。");
        }

        var destination = Path.GetFullPath(destinationPath);
        if (IsInsideDataDirectory(destination))
        {
            // データディレクトリ内に置くと世代整理や一時ファイル掃除の対象と取り違えるため拒否する
            throw new VaultStorageException(VaultStorageError.InvalidDestination, "バックアップは保管庫のデータディレクトリの外に保存してください。");
        }

        if (!overwrite && _fs.FileExists(destination))
        {
            throw new VaultStorageException(VaultStorageError.AlreadyExists, "コピー先に同名のファイルが既にあります。");
        }

        try
        {
            var directory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(directory))
            {
                _fs.CreateDirectory(directory);
            }

            _fs.CopyFile(Paths.VaultFile, destination, overwrite);
            VerifyCopy(destination);
            return destination;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new VaultStorageException(VaultStorageError.CopyFailed, "バックアップのコピーに失敗しました。", ex);
        }
    }

    /// <summary>Reads the manual backup back and compares it with the vault byte for byte; a mismatch is removed and reported (the copy is the user's disaster-recovery file).</summary>
    private void VerifyCopy(string destination)
    {
        var source = _fs.ReadAllBytes(Paths.VaultFile);
        var copy = _fs.ReadAllBytes(destination);
        if (!source.AsSpan().SequenceEqual(copy))
        {
            TryDelete(destination);
            throw new VaultStorageException(VaultStorageError.CopyFailed, "バックアップのコピーを読み戻して照合したところ、内容が一致しませんでした。コピーは削除しました。別の保存先を試してください。");
        }
    }

    /// <summary>Saves a copy of the current vault before a restore replaces it. Not counted as a generation.</summary>
    public string SavePreRestoreCopy()
    {
        if (!Exists)
        {
            throw new VaultStorageException(VaultStorageError.NotFound, "保管庫ファイルが見つかりません。");
        }

        var target = Paths.NewPreRestoreFile(_clock.UtcNow);
        try
        {
            EnsureDirectories();
            _fs.CopyFile(Paths.VaultFile, target, overwrite: false);
            return target;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new VaultStorageException(VaultStorageError.CopyFailed, "復元前の退避コピーを作成できませんでした。", ex);
        }
    }

    private void EnsureDirectories()
    {
        _fs.CreateDirectory(Paths.DataDirectory);
        _fs.CreateDirectory(Paths.BackupDirectory);
    }

    private void EnsureDirectoriesForWrite()
    {
        try
        {
            EnsureDirectories();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new VaultStorageException(VaultStorageError.WriteFailed, "保管庫のフォルダーを作成できませんでした。", ex);
        }
    }

    /// <summary>The next generation's path. Naming reads the backup folder; any unexpected failure there is reported as a storage error instead of escaping.</summary>
    private string NewGenerationFile()
    {
        try
        {
            return Paths.NewGenerationFile(NextGenerationStamp(), Interlocked.Increment(ref _generationSequence));
        }
        catch (Exception ex) when (ex is not VaultStorageException)
        {
            throw new VaultStorageException(VaultStorageError.WriteFailed, "世代バックアップの名前を決められませんでした。", ex);
        }
    }

    /// <summary>Clock time, or one millisecond after the newest existing generation when the clock is behind it, so names stay in chronological order (D-009).</summary>
    private DateTimeOffset NextGenerationStamp()
    {
        var now = _clock.UtcNow;
        var newest = ListGenerationFiles().FirstOrDefault();
        if (newest is not null && VaultPaths.TryParseGenerationStamp(newest, out var latest) && latest >= now)
        {
            // 表現できる上限（年 9999）の名前が置かれていても加算で例外にせず、同じ時刻で命名する（連番と乱数で区別される）
            return latest < MaxStamp ? latest.AddMilliseconds(1) : latest;
        }

        return now;
    }

    private static readonly DateTimeOffset MaxStamp = DateTimeOffset.MaxValue.AddMilliseconds(-1);

    private bool IsInsideDataDirectory(string fullPath)
    {
        var root = Paths.DataDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private bool TryMove(string source, string destination)
    {
        try
        {
            _fs.MoveFile(source, destination);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void WriteTemp(string temp, byte[] encrypted)
    {
        try
        {
            _fs.WriteNewFileDurably(temp, encrypted);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(temp);
            throw new VaultStorageException(VaultStorageError.WriteFailed, "保管庫の一時ファイルを書き込めませんでした。", ex);
        }
    }

    private void PruneGenerations(int keep)
    {
        try
        {
            foreach (var old in ListGenerationFiles().Skip(keep))
            {
                _fs.DeleteFile(old);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 古い世代の削除失敗は次回の保存で再試行される
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            _fs.DeleteFile(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 一時ファイルの削除失敗は致命的ではない（暗号化済みのため）
        }
    }
}
