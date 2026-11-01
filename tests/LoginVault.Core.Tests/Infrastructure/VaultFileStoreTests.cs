using LoginVault.Infrastructure.Crypto;
using LoginVault.Infrastructure.Storage;

namespace LoginVault.Core.Tests.Infrastructure;

public class VaultFileStoreTests : IDisposable
{
    private readonly TempDataDir _dir = new();
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 10, 28, 3, 0, 0, TimeSpan.Zero));

    private VaultFileStore CreateStore(IVaultFileSystem? fs = null) =>
        new(fs ?? new PhysicalFileSystem(), new VaultPaths(_dir.Path), _clock);

    private static byte[] Content(int seed) => Enumerable.Range(0, 64).Select(i => (byte)(i + seed)).ToArray();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void WriteNew_CreatesVaultFile_WithoutLeavingTempFiles()
    {
        var store = CreateStore();

        store.WriteNew(Content(1));

        Assert.True(store.Exists);
        Assert.Equal(Content(1), store.Read());
        Assert.Empty(Directory.GetFiles(_dir.Path, "*.tmp"));
    }

    [Fact]
    public void WriteNew_WhenVaultExists_Throws_AndKeepsContent()
    {
        var store = CreateStore();
        store.WriteNew(Content(1));

        var ex = Assert.Throws<VaultStorageException>(() => store.WriteNew(Content(2)));

        Assert.Equal(VaultStorageError.AlreadyExists, ex.Error);
        Assert.Equal(Content(1), store.Read());
        Assert.Empty(Directory.GetFiles(_dir.Path, "*.tmp"));
    }

    [Fact]
    public void Replace_WritesNewContent_AndKeepsPreviousVersionAsGeneration()
    {
        var store = CreateStore();
        store.WriteNew(Content(1));

        _clock.Advance(TimeSpan.FromSeconds(1));
        store.Replace(Content(2), generationsToKeep: 10);

        Assert.Equal(Content(2), store.Read());
        var generation = Assert.Single(store.ListGenerationFiles());
        Assert.Equal(Content(1), File.ReadAllBytes(generation));
        Assert.Empty(Directory.GetFiles(_dir.Path, "*.tmp"));
    }

    [Fact]
    public void Replace_KeepsOnlyTheNewestGenerations()
    {
        var store = CreateStore();
        store.WriteNew(Content(0));
        for (var i = 1; i <= 13; i++)
        {
            _clock.Advance(TimeSpan.FromSeconds(1));
            store.Replace(Content(i), generationsToKeep: 10);
        }

        var generations = store.ListGenerationFiles();

        Assert.Equal(10, generations.Count);
        Assert.Equal(Content(12), File.ReadAllBytes(generations[0]));
        Assert.Equal(Content(3), File.ReadAllBytes(generations[^1]));
        Assert.Equal(Content(13), store.Read());
    }

    [Fact]
    public void Replace_WhenReplaceFails_KeepsCurrentContent_AndRemovesTemp()
    {
        var fs = new FaultyFileSystem();
        var store = CreateStore(fs);
        store.WriteNew(Content(1));
        fs.FailReplace = true;

        var ex = Assert.Throws<VaultStorageException>(() => store.Replace(Content(2), 10));

        Assert.Equal(VaultStorageError.ReplaceFailed, ex.Error);
        Assert.Equal(Content(1), store.Read());
        Assert.Empty(Directory.GetFiles(_dir.Path, "*.tmp"));
        Assert.Empty(store.ListGenerationFiles());
    }

    [Fact]
    public void Replace_WhenTempWriteFails_KeepsCurrentContent()
    {
        var fs = new FaultyFileSystem();
        var store = CreateStore(fs);
        store.WriteNew(Content(1));
        fs.FailWriteNew = true;

        var ex = Assert.Throws<VaultStorageException>(() => store.Replace(Content(2), 10));

        Assert.Equal(VaultStorageError.WriteFailed, ex.Error);
        Assert.Equal(Content(1), store.Read());
        Assert.Equal(0, fs.ReplaceCalls);
    }

    [Fact]
    public void Replace_WhenVaultMissing_Throws()
    {
        var store = CreateStore();

        var ex = Assert.Throws<VaultStorageException>(() => store.Replace(Content(2), 10));

        Assert.Equal(VaultStorageError.NotFound, ex.Error);
    }

    [Fact]
    public void Read_WhenMissing_Throws()
    {
        var ex = Assert.Throws<VaultStorageException>(() => CreateStore().Read());

        Assert.Equal(VaultStorageError.NotFound, ex.Error);
    }

    [Fact]
    public void Read_FileLargerThanLimit_IsRejectedWithoutReadingIt()
    {
        var paths = new VaultPaths(_dir.Path);
        using (var fs = new FileStream(paths.VaultFile, FileMode.CreateNew))
        {
            fs.SetLength(VaultFormat.MaxFileLength + 1);
        }

        var ex = Assert.Throws<VaultFormatException>(() => CreateStore().Read());

        Assert.Equal(VaultFormatError.FileTooLarge, ex.Error);
    }

    [Fact]
    public void CopyTo_CreatesIdenticalCopy_AndRefusesOverwriteByDefault()
    {
        var store = CreateStore();
        store.WriteNew(Content(1));
        using var other = new TempDataDir();
        var destination = Path.Combine(other.Path, "manual-backup.dat");

        store.CopyTo(destination, overwrite: false);

        Assert.Equal(Content(1), File.ReadAllBytes(destination));
        var ex = Assert.Throws<VaultStorageException>(() => store.CopyTo(destination, overwrite: false));
        Assert.Equal(VaultStorageError.AlreadyExists, ex.Error);
        store.Replace(Content(2), generationsToKeep: 1);
        store.CopyTo(destination, overwrite: true);
        Assert.Equal(Content(2), File.ReadAllBytes(destination));
    }

    [Fact]
    public void SavePreRestoreCopy_IsStoredInBackups_ButNotCountedAsGeneration()
    {
        var store = CreateStore();
        store.WriteNew(Content(1));

        var copy = store.SavePreRestoreCopy();

        Assert.True(File.Exists(copy));
        Assert.Equal(Content(1), File.ReadAllBytes(copy));
        Assert.StartsWith(Path.Combine(_dir.Path, "backups"), copy, StringComparison.Ordinal);
        Assert.Empty(store.ListGenerationFiles());
    }

    [Fact]
    public void GenerationFiles_RemainOrderedWhenClockDoesNotAdvance()
    {
        var store = CreateStore();
        store.WriteNew(Content(0));
        for (var i = 1; i <= 5; i++)
        {
            store.Replace(Content(i), 10);
        }

        var generations = store.ListGenerationFiles();

        Assert.Equal(new[] { Content(4), Content(3), Content(2), Content(1), Content(0) }, generations.Select(File.ReadAllBytes).ToArray());
    }

    [Fact]
    public void GenerationFiles_AreListedNewestFirst()
    {
        var store = CreateStore();
        store.WriteNew(Content(0));
        for (var i = 1; i <= 3; i++)
        {
            _clock.Advance(TimeSpan.FromMinutes(1));
            store.Replace(Content(i), 10);
        }

        var generations = store.ListGenerationFiles();

        Assert.Equal(new[] { Content(2), Content(1), Content(0) }, generations.Select(File.ReadAllBytes).ToArray());
    }

    [Fact]
    public void Replace_WhenReplaceFailsAfterMovingTheOldVersionAside_PromotesTheNewContent()
    {
        var fs = new FaultyFileSystem();
        var store = CreateStore(fs);
        store.WriteNew(Content(1));
        fs.FailReplaceAfterBackup = true;

        store.Replace(Content(2), generationsToKeep: 5);

        Assert.Equal(Content(2), store.Read());
        var generations = store.ListGenerationFiles();
        Assert.Single(generations);
        Assert.Equal(Content(1), File.ReadAllBytes(generations[0]));
        Assert.Empty(Directory.GetFiles(_dir.Path, "*.tmp"));
    }

    [Fact]
    public void Replace_WhenReplaceFailsMidwayAndRecoveryFails_KeepsBothVersions_AndExplains()
    {
        var fs = new FaultyFileSystem();
        var store = CreateStore(fs);
        store.WriteNew(Content(1));
        fs.FailReplaceAfterBackup = true;
        fs.FailMove = true;

        var ex = Assert.Throws<VaultStorageException>(() => store.Replace(Content(2), generationsToKeep: 5));

        Assert.Equal(VaultStorageError.ReplaceFailed, ex.Error);
        Assert.Contains("一時ファイル", ex.Message, StringComparison.Ordinal);
        Assert.False(store.Exists);
        var temp = Assert.Single(Directory.GetFiles(_dir.Path, "*.tmp"));
        Assert.Equal(Content(2), File.ReadAllBytes(temp));
        Assert.Equal(Content(1), File.ReadAllBytes(Assert.Single(store.ListGenerationFiles())));
    }

    [Fact]
    public void WriteNew_WhenMoveFails_ReportsWriteFailed_AndRemovesTemp()
    {
        var store = CreateStore(new FaultyFileSystem { FailMove = true });

        var ex = Assert.Throws<VaultStorageException>(() => store.WriteNew(Content(1)));

        Assert.Equal(VaultStorageError.WriteFailed, ex.Error);
        Assert.False(store.Exists);
        Assert.Empty(Directory.GetFiles(_dir.Path, "*.tmp"));
    }

    [Fact]
    public void WriteNew_WhenDirectoryCannotBeCreated_ReportsWriteFailed()
    {
        var store = CreateStore(new FaultyFileSystem { FailCreateDirectory = true });

        var ex = Assert.Throws<VaultStorageException>(() => store.WriteNew(Content(1)));

        Assert.Equal(VaultStorageError.WriteFailed, ex.Error);
    }

    [Fact]
    public void CopyTo_RefusesDestinationsInsideTheDataDirectory()
    {
        var store = CreateStore();
        store.WriteNew(Content(1));
        var destinations = new[]
        {
            Path.Combine(_dir.Path, "vault.dat.copy.tmp"),
            Path.Combine(_dir.Path, "backups", "vault-manual.dat"),
            Path.Combine(_dir.Path, "sub", "copy.dat"),
        };

        foreach (var destination in destinations)
        {
            var ex = Assert.Throws<VaultStorageException>(() => store.CopyTo(destination, overwrite: false));
            Assert.Equal(VaultStorageError.InvalidDestination, ex.Error);
            Assert.False(File.Exists(destination));
        }
    }

    [Fact]
    public void NewGeneration_SortsAfterAFutureDatedGeneration_WhenTheClockIsBehind()
    {
        var store = CreateStore();
        store.WriteNew(Content(1));
        Directory.CreateDirectory(Path.Combine(_dir.Path, "backups"));
        var future = Path.Combine(_dir.Path, "backups", "vault-20300101T000000000Z-000001-ffffffff.dat");
        File.WriteAllBytes(future, Content(9));

        store.Replace(Content(2), generationsToKeep: 10);

        var generations = store.ListGenerationFiles();
        Assert.Equal(2, generations.Count);
        Assert.NotEqual(future, generations[0]);
        Assert.Equal(Content(1), File.ReadAllBytes(generations[0]));
    }

    [Fact]
    public void HasRecoverableFiles_DetectsGenerations_PreRestoreCopies_AndStaleTempFiles()
    {
        var store = CreateStore();
        Assert.False(store.HasRecoverableFiles);
        Directory.CreateDirectory(Path.Combine(_dir.Path, "backups"));

        var generation = Path.Combine(_dir.Path, "backups", "vault-20261028T030000000Z-000001-00000000.dat");
        File.WriteAllBytes(generation, Content(1));
        Assert.True(store.HasRecoverableFiles);
        File.Delete(generation);

        var preRestore = Path.Combine(_dir.Path, "backups", "pre-restore-20261028T030000000Z-00000000.dat");
        File.WriteAllBytes(preRestore, Content(1));
        Assert.True(store.HasRecoverableFiles);
        File.Delete(preRestore);

        var temp = Path.Combine(_dir.Path, "vault.dat.deadbeef.tmp");
        File.WriteAllBytes(temp, Content(1));
        Assert.True(store.HasRecoverableFiles);
        File.Delete(temp);

        File.WriteAllText(Path.Combine(_dir.Path, "ui-settings.json"), "{\"theme\":\"sf\"}");
        Assert.False(store.HasRecoverableFiles);
    }

    [Fact]
    public void Replace_LeavesStrayBackupFilesAlone_AndNeverCountsThemAsGenerations()
    {
        // 世代名の形式に合わない vault-*.dat（利用者の手動コピーなど）は、整理の対象にも「最新の世代」にもしない
        var store = CreateStore();
        store.WriteNew(Content(0));
        var backups = Path.Combine(_dir.Path, "backups");
        Directory.CreateDirectory(backups);
        var strays = new[] { "vault-backup.dat", "vault-zz1.dat", "vault-zz2.dat" }.Select(n => Path.Combine(backups, n)).ToArray();
        foreach (var stray in strays)
        {
            File.WriteAllBytes(stray, Content(9));
        }

        for (var i = 1; i <= 3; i++)
        {
            store.Replace(Content(i), generationsToKeep: 2);
        }

        var generations = store.ListGenerationFiles();
        Assert.Equal(new[] { Content(2), Content(1) }, generations.Select(File.ReadAllBytes).ToArray());
        Assert.All(strays, stray => Assert.Equal(Content(9), File.ReadAllBytes(stray)));
    }

    [Fact]
    public void HasRecoverableFiles_CountsStrayBackupCopies_ThatAreNotGenerations()
    {
        var store = CreateStore();
        var backups = Path.Combine(_dir.Path, "backups");
        Directory.CreateDirectory(backups);
        File.WriteAllBytes(Path.Combine(backups, "vault-backup.dat"), Content(1));

        Assert.Empty(store.ListGenerationFiles());
        Assert.True(store.HasRecoverableFiles);
    }

    [Fact]
    public void Replace_WhenABackupNameCarriesTheLargestPossibleTimestamp_StillSaves()
    {
        // 年 9999 の名前が置かれていても、採番がオーバーフローして保存が止まらない
        var store = CreateStore();
        store.WriteNew(Content(1));
        var backups = Path.Combine(_dir.Path, "backups");
        Directory.CreateDirectory(backups);
        var farFuture = Path.Combine(backups, "vault-99991231T235959999Z-000001-ffffffff.dat");
        File.WriteAllBytes(farFuture, Content(9));

        store.Replace(Content(2), generationsToKeep: 10);

        Assert.Equal(Content(2), File.ReadAllBytes(Path.Combine(_dir.Path, "vault.dat")));
        Assert.Contains(store.ListGenerationFiles(), g => File.ReadAllBytes(g).SequenceEqual(Content(1)));
        Assert.True(File.Exists(farFuture));
    }

    [Fact]
    public void CopyTo_ReadsTheCopyBack_AndReportsAMismatch_WithoutLeavingIt()
    {
        var fs = new FaultyFileSystem { CorruptCopy = true };
        var store = CreateStore(fs);
        store.WriteNew(Content(1));
        using var elsewhere = new TempDataDir();
        var destination = Path.Combine(elsewhere.Path, "manual-backup.dat");

        var ex = Assert.Throws<VaultStorageException>(() => store.CopyTo(destination, overwrite: false));

        Assert.Equal(VaultStorageError.CopyFailed, ex.Error);
        Assert.False(File.Exists(destination));
    }
}
