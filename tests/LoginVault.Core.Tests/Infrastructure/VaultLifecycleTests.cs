using LoginVault.Core.Model;
using LoginVault.Core.Operations;
using LoginVault.Core.Tests.Core;
using LoginVault.Core.Validation;
using LoginVault.Infrastructure.Crypto;
using LoginVault.Infrastructure.Serialization;
using LoginVault.Infrastructure.Session;
using LoginVault.Infrastructure.Storage;

namespace LoginVault.Core.Tests.Infrastructure;

public class VaultLifecycleTests : IDisposable
{
    private readonly TempDataDir _dir = new();
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 10, 28, 3, 0, 0, TimeSpan.Zero));

    public void Dispose() => _dir.Dispose();

    private VaultLifecycle CreateLifecycle(IVaultFileSystem? fs = null) =>
        new(new VaultFileStore(fs ?? new PhysicalFileSystem(), new VaultPaths(_dir.Path), _clock), _clock);

    private static Func<VaultDocument, OperationResult> AddService(string name) =>
        doc => VaultOperations.AddService(doc, name, Fx.Now);

    private static Func<VaultDocument, OperationResult> AddSentinelAccount(Guid serviceId) =>
        doc => VaultOperations.AddAccount(
            doc,
            Fx.Input(serviceId, Secrets.LoginId, PasswordChange.Set(Secrets.AccountPassword), notes: Secrets.Notes),
            Fx.Now);

    [Fact]
    public async Task Initialize_CreatesVaultAndUnlocksEmptySession()
    {
        using var lifecycle = CreateLifecycle();
        Assert.False(lifecycle.VaultExists);

        var snapshot = await lifecycle.InitializeAsync(Secrets.MasterPassword);

        Assert.True(lifecycle.VaultExists);
        Assert.True(lifecycle.IsUnlocked);
        Assert.NotEqual(Guid.Empty, snapshot.SessionToken);
        Assert.Empty(snapshot.Document.Services);
        Assert.Same(snapshot, lifecycle.Current);
    }

    [Fact]
    public async Task Initialize_WhenVaultExists_Throws_AndDoesNotOverwrite()
    {
        using (var first = CreateLifecycle())
        {
            var s = await first.InitializeAsync(Secrets.MasterPassword);
            await first.CommitAsync(s.SessionToken, AddService("既存"));
        }

        using var second = CreateLifecycle();
        var ex = await Assert.ThrowsAsync<VaultStorageException>(() => second.InitializeAsync(Secrets.OtherPassword));

        Assert.Equal(VaultStorageError.AlreadyExists, ex.Error);
        var snapshot = await second.UnlockAsync(Secrets.MasterPassword);
        Assert.Single(snapshot.Document.Services);
    }

    [Theory]
    [InlineData("")]
    public async Task Initialize_EmptyPassword_IsRejected(string password)
    {
        using var lifecycle = CreateLifecycle();

        await Assert.ThrowsAsync<ArgumentException>(() => lifecycle.InitializeAsync(password));
        Assert.False(lifecycle.VaultExists);
    }

    [Fact]
    public async Task Commit_PersistsChanges_AndUnlockReadsThemBack()
    {
        using var lifecycle = CreateLifecycle();
        var s = await lifecycle.InitializeAsync(Secrets.MasterPassword);

        var result = await lifecycle.CommitAsync(s.SessionToken, AddService("Google"));
        Assert.Equal(CommitStatus.Saved, result.Status);
        Assert.Same(result.Snapshot, lifecycle.Current);
        await lifecycle.LockAsync();

        var reopened = await lifecycle.UnlockAsync(Secrets.MasterPassword);
        Assert.Equal("Google", Assert.Single(reopened.Document.Services).Name);
        Assert.NotEqual(s.SessionToken, reopened.SessionToken);
    }

    [Fact]
    public async Task Unlock_WrongPassword_Throws_AndFileIsUntouched()
    {
        using var lifecycle = CreateLifecycle();
        await lifecycle.InitializeAsync(Secrets.MasterPassword);
        await lifecycle.LockAsync();
        var before = File.ReadAllBytes(new VaultPaths(_dir.Path).VaultFile);

        await Assert.ThrowsAsync<VaultAuthenticationException>(() => lifecycle.UnlockAsync(Secrets.OtherPassword));

        Assert.False(lifecycle.IsUnlocked);
        Assert.Equal(before, File.ReadAllBytes(new VaultPaths(_dir.Path).VaultFile));
    }

    [Fact]
    public async Task Unlock_TamperedFile_Throws_AndIsNeverReinitialized()
    {
        using var lifecycle = CreateLifecycle();
        await lifecycle.InitializeAsync(Secrets.MasterPassword);
        await lifecycle.LockAsync();
        var path = new VaultPaths(_dir.Path).VaultFile;
        var bytes = File.ReadAllBytes(path);
        bytes[^1] ^= 0x01;
        File.WriteAllBytes(path, bytes);

        await Assert.ThrowsAsync<VaultAuthenticationException>(() => lifecycle.UnlockAsync(Secrets.MasterPassword));

        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.True(lifecycle.VaultExists);
    }

    [Fact]
    public async Task Commit_ValidationFailure_IsRejected_AndNothingIsWritten()
    {
        using var lifecycle = CreateLifecycle();
        var s = await lifecycle.InitializeAsync(Secrets.MasterPassword);
        var path = new VaultPaths(_dir.Path).VaultFile;
        var before = File.ReadAllBytes(path);

        var result = await lifecycle.CommitAsync(s.SessionToken, AddService("   "));

        Assert.Equal(CommitStatus.Rejected, result.Status);
        Assert.Contains(result.Issues, i => i.Code == IssueCode.ServiceNameRequired);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Empty(lifecycle.Current!.Document.Services);
    }

    [Fact]
    public async Task Commit_StorageFailure_IsReported_AndDocumentStaysUnchanged()
    {
        var fs = new FaultyFileSystem();
        using var lifecycle = CreateLifecycle(fs);
        var s = await lifecycle.InitializeAsync(Secrets.MasterPassword);
        var ok = await lifecycle.CommitAsync(s.SessionToken, AddService("A"));
        fs.FailReplace = true;

        var failed = await lifecycle.CommitAsync(ok.Snapshot!.SessionToken, AddService("B"));

        Assert.Equal(CommitStatus.Failed, failed.Status);
        Assert.IsType<VaultStorageException>(failed.Error);
        Assert.Equal("A", Assert.Single(lifecycle.Current!.Document.Services).Name);
        fs.FailReplace = false;
        await lifecycle.LockAsync();
        var reopened = await lifecycle.UnlockAsync(Secrets.MasterPassword);
        Assert.Equal("A", Assert.Single(reopened.Document.Services).Name);
    }

    [Fact]
    public async Task Commit_WithStaleToken_IsIgnored()
    {
        using var lifecycle = CreateLifecycle();
        var first = await lifecycle.InitializeAsync(Secrets.MasterPassword);
        await lifecycle.LockAsync();
        var second = await lifecycle.UnlockAsync(Secrets.MasterPassword);

        var stale = await lifecycle.CommitAsync(first.SessionToken, AddService("古いセッション"));
        var fresh = await lifecycle.CommitAsync(second.SessionToken, AddService("新しいセッション"));

        Assert.Equal(CommitStatus.StaleSession, stale.Status);
        Assert.Equal(CommitStatus.Saved, fresh.Status);
        Assert.Equal("新しいセッション", Assert.Single(lifecycle.Current!.Document.Services).Name);
    }

    [Fact]
    public async Task Commit_WhileLocked_IsStale()
    {
        using var lifecycle = CreateLifecycle();
        var s = await lifecycle.InitializeAsync(Secrets.MasterPassword);
        await lifecycle.LockAsync();

        var result = await lifecycle.CommitAsync(s.SessionToken, AddService("X"));

        Assert.Equal(CommitStatus.StaleSession, result.Status);
        Assert.Null(lifecycle.Current);
    }

    [Fact]
    public async Task ConcurrentCommits_AreSerialized_AndAllApplied()
    {
        using var lifecycle = CreateLifecycle();
        var s = await lifecycle.InitializeAsync(Secrets.MasterPassword);

        var tasks = Enumerable.Range(0, 20).Select(i => lifecycle.CommitAsync(s.SessionToken, AddService($"S{i:D2}"))).ToArray();
        var results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.Equal(CommitStatus.Saved, r.Status));
        Assert.Equal(20, lifecycle.Current!.Document.Services.Count);
        await lifecycle.LockAsync();
        var reopened = await lifecycle.UnlockAsync(Secrets.MasterPassword);
        Assert.Equal(20, reopened.Document.Services.Count);
        Assert.Equal(10, new VaultFileStore(new PhysicalFileSystem(), new VaultPaths(_dir.Path), _clock).ListGenerationFiles().Count);
    }

    [Fact]
    public async Task Lock_WaitsForInFlightCommit_AndTheCommitIsPersisted()
    {
        var fs = new GatedFileSystem();
        using var lifecycle = CreateLifecycle(fs);
        var s = await lifecycle.InitializeAsync(Secrets.MasterPassword);
        fs.BlockNextReplace = true;

        var commit = lifecycle.CommitAsync(s.SessionToken, AddService("保存中"));
        Assert.True(fs.ReplaceEntered.Wait(TimeSpan.FromSeconds(10)), "保存処理が開始されませんでした。");
        var lockTask = lifecycle.LockAsync();
        await Task.Delay(200);
        Assert.False(lockTask.IsCompleted, "保存中にロックが完了してはいけません。");
        Assert.True(lifecycle.IsUnlocked);

        fs.ReleaseReplace.Set();
        var result = await commit;
        await lockTask;

        Assert.Equal(CommitStatus.Saved, result.Status);
        Assert.False(lifecycle.IsUnlocked);
        var reopened = await lifecycle.UnlockAsync(Secrets.MasterPassword);
        Assert.Equal("保存中", Assert.Single(reopened.Document.Services).Name);
    }

    [Fact]
    public async Task OldSessionWork_CompletingAfterRelock_CannotOverwriteNewSession()
    {
        var fs = new GatedFileSystem();
        using var lifecycle = CreateLifecycle(fs);
        var first = await lifecycle.InitializeAsync(Secrets.MasterPassword);
        fs.BlockNextReplace = true;
        var inFlight = lifecycle.CommitAsync(first.SessionToken, AddService("A"));
        Assert.True(fs.ReplaceEntered.Wait(TimeSpan.FromSeconds(10)));
        var lockTask = lifecycle.LockAsync();
        var lateCommit = lifecycle.CommitAsync(first.SessionToken, AddService("遅延"));
        fs.ReleaseReplace.Set();
        await inFlight;
        await lockTask;
        var late = await lateCommit;

        var second = await lifecycle.UnlockAsync(Secrets.MasterPassword);
        var fresh = await lifecycle.CommitAsync(second.SessionToken, AddService("B"));
        var staleAgain = await lifecycle.CommitAsync(first.SessionToken, AddService("C"));

        Assert.Equal(CommitStatus.StaleSession, late.Status);
        Assert.Equal(CommitStatus.Saved, fresh.Status);
        Assert.Equal(CommitStatus.StaleSession, staleAgain.Status);
        Assert.Equal(new[] { "A", "B" }, lifecycle.Current!.Document.Services.Select(x => x.Name).ToArray());
    }

    [Fact]
    public async Task ChangeMasterPassword_NewPasswordOpensVault_OldDoesNot_ButOldGenerationStillUsesOld()
    {
        using var lifecycle = CreateLifecycle();
        var s = await lifecycle.InitializeAsync(Secrets.MasterPassword);
        var committed = await lifecycle.CommitAsync(s.SessionToken, AddService("A"));

        var changed = await lifecycle.ChangeMasterPasswordAsync(committed.Snapshot!.SessionToken, Secrets.OtherPassword);
        Assert.Equal(CommitStatus.Saved, changed.Status);
        var after = await lifecycle.CommitAsync(changed.Snapshot!.SessionToken, AddService("B"));
        Assert.Equal(CommitStatus.Saved, after.Status);
        await lifecycle.LockAsync();

        await Assert.ThrowsAsync<VaultAuthenticationException>(() => lifecycle.UnlockAsync(Secrets.MasterPassword));
        var reopened = await lifecycle.UnlockAsync(Secrets.OtherPassword);
        Assert.Equal(2, reopened.Document.Services.Count);

        // 旧世代のバックアップは旧パスワードのまま（利用者へ警告する根拠）
        var generations = new VaultFileStore(new PhysicalFileSystem(), new VaultPaths(_dir.Path), _clock).ListGenerationFiles();
        var oldest = generations[^1];
        var plain = VaultCipher.DecryptWithPassword(Secrets.MasterPassword, File.ReadAllBytes(oldest), out var oldKey);
        oldKey.Dispose();
        Assert.NotEmpty(plain);
    }

    [Fact]
    public async Task ChangeMasterPassword_WhenWriteFails_KeepsOldPassword()
    {
        var fs = new FaultyFileSystem();
        using var lifecycle = CreateLifecycle(fs);
        var s = await lifecycle.InitializeAsync(Secrets.MasterPassword);
        fs.FailReplace = true;

        var result = await lifecycle.ChangeMasterPasswordAsync(s.SessionToken, Secrets.OtherPassword);
        Assert.Equal(CommitStatus.Failed, result.Status);
        fs.FailReplace = false;
        var stillWorks = await lifecycle.CommitAsync(s.SessionToken, AddService("A"));
        Assert.Equal(CommitStatus.Saved, stillWorks.Status);
        await lifecycle.LockAsync();

        await Assert.ThrowsAsync<VaultAuthenticationException>(() => lifecycle.UnlockAsync(Secrets.OtherPassword));
        var reopened = await lifecycle.UnlockAsync(Secrets.MasterPassword);
        Assert.Single(reopened.Document.Services);
    }

    [Fact]
    public async Task ChangeMasterPassword_EmptyPassword_IsRejected()
    {
        using var lifecycle = CreateLifecycle();
        var s = await lifecycle.InitializeAsync(Secrets.MasterPassword);

        await Assert.ThrowsAsync<ArgumentException>(() => lifecycle.ChangeMasterPasswordAsync(s.SessionToken, ""));
    }

    [Fact]
    public async Task ManualBackup_ThenRestoreFromAnotherDirectory_ReplacesDocument_AndKeepsPreRestoreCopy()
    {
        using var lifecycle = CreateLifecycle();
        using var other = new TempDataDir();
        var s = await lifecycle.InitializeAsync(Secrets.MasterPassword);
        var withA = await lifecycle.CommitAsync(s.SessionToken, AddService("A"));
        var backupPath = Path.Combine(other.Path, "manual", "backup.dat");
        var written = await lifecycle.CreateManualBackupAsync(withA.Snapshot!.SessionToken, backupPath, overwrite: false);
        Assert.Equal(backupPath, written);
        var withB = await lifecycle.CommitAsync(withA.Snapshot.SessionToken, AddService("B"));
        Assert.Equal(2, withB.Snapshot!.Document.Services.Count);

        var restored = await lifecycle.RestoreAsync(withB.Snapshot.SessionToken, backupPath, Secrets.MasterPassword);

        Assert.Equal(CommitStatus.Saved, restored.Status);
        Assert.Equal("A", Assert.Single(restored.Snapshot!.Document.Services).Name);
        var backups = Directory.GetFiles(Path.Combine(_dir.Path, "backups"), "pre-restore-*.dat");
        var preRestore = Assert.Single(backups);
        var preRestorePlain = VaultCipher.DecryptWithPassword(Secrets.MasterPassword, File.ReadAllBytes(preRestore), out var key);
        key.Dispose();
        Assert.Equal(2, VaultSerializer.Deserialize(preRestorePlain).Services.Count);
        await lifecycle.LockAsync();
        var reopened = await lifecycle.UnlockAsync(Secrets.MasterPassword);
        Assert.Equal("A", Assert.Single(reopened.Document.Services).Name);
    }

    [Fact]
    public async Task Restore_WithWrongBackupPassword_Fails_AndChangesNothing()
    {
        using var lifecycle = CreateLifecycle();
        using var other = new TempDataDir();
        var s = await lifecycle.InitializeAsync(Secrets.MasterPassword);
        var withA = await lifecycle.CommitAsync(s.SessionToken, AddService("A"));
        var backupPath = Path.Combine(other.Path, "backup.dat");
        await lifecycle.CreateManualBackupAsync(withA.Snapshot!.SessionToken, backupPath, overwrite: false);
        var withB = await lifecycle.CommitAsync(withA.Snapshot.SessionToken, AddService("B"));

        var result = await lifecycle.RestoreAsync(withB.Snapshot!.SessionToken, backupPath, Secrets.OtherPassword);

        Assert.Equal(CommitStatus.Failed, result.Status);
        Assert.IsType<VaultAuthenticationException>(result.Error);
        Assert.Equal(2, lifecycle.Current!.Document.Services.Count);
        Assert.Empty(Directory.GetFiles(Path.Combine(_dir.Path, "backups"), "pre-restore-*.dat"));
    }

    [Fact]
    public async Task Restore_BackupWithBrokenReferences_IsRejected()
    {
        using var lifecycle = CreateLifecycle();
        using var other = new TempDataDir();
        var s = await lifecycle.InitializeAsync(Secrets.MasterPassword);
        var service = Fx.Service("A");
        var invalid = Fx.Doc(new[] { service }, new[] { Fx.Reference(service.Id, Guid.NewGuid()) });
        using var key = VaultKey.Derive(Secrets.MasterPassword, KdfParameters.CreateDefault());
        var corruptBackup = Path.Combine(other.Path, "broken.dat");
        File.WriteAllBytes(corruptBackup, VaultCipher.Encrypt(key, VaultSerializer.Serialize(invalid)));

        var result = await lifecycle.RestoreAsync(s.SessionToken, corruptBackup, Secrets.MasterPassword);

        Assert.Equal(CommitStatus.Rejected, result.Status);
        Assert.Contains(result.Issues, i => i.Code == IssueCode.ReferenceTargetMissing);
        Assert.Empty(lifecycle.Current!.Document.Services);
    }

    [Fact]
    public async Task Restore_WithStaleToken_IsStale()
    {
        using var lifecycle = CreateLifecycle();
        var s = await lifecycle.InitializeAsync(Secrets.MasterPassword);
        await lifecycle.LockAsync();
        await lifecycle.UnlockAsync(Secrets.MasterPassword);

        var result = await lifecycle.RestoreAsync(s.SessionToken, Path.Combine(_dir.Path, "none.dat"), Secrets.MasterPassword);

        Assert.Equal(CommitStatus.StaleSession, result.Status);
    }

    [Fact]
    public async Task VerifyMasterPassword_IsTrueOnlyForTheCurrentPasswordAndSession()
    {
        using var lifecycle = CreateLifecycle();
        var s = await lifecycle.InitializeAsync(Secrets.MasterPassword);

        Assert.True(await lifecycle.VerifyMasterPasswordAsync(s.SessionToken, Secrets.MasterPassword));
        Assert.False(await lifecycle.VerifyMasterPasswordAsync(s.SessionToken, Secrets.OtherPassword));
        Assert.False(await lifecycle.VerifyMasterPasswordAsync(s.SessionToken, Secrets.MasterPassword + " "));
        Assert.False(await lifecycle.VerifyMasterPasswordAsync(Guid.NewGuid(), Secrets.MasterPassword));
        await lifecycle.LockAsync();
        Assert.False(await lifecycle.VerifyMasterPasswordAsync(s.SessionToken, Secrets.MasterPassword));
    }

    [Fact]
    public async Task Restore_PrunesWithTheLargerGenerationSetting_NotTheBackupsSmallerOne()
    {
        using var lifecycle = CreateLifecycle();
        using var other = new TempDataDir();
        var s = await lifecycle.InitializeAsync(Secrets.MasterPassword);
        var token = s.SessionToken;
        await lifecycle.CommitAsync(token, AddService("A"));
        await lifecycle.CommitAsync(token, AddService("B"));
        await lifecycle.CommitAsync(token, AddService("C"));
        Assert.Equal(3, lifecycle.ListGenerationFiles().Count);
        var smallSetting = VaultDocument.Empty with { Preferences = new VaultPreferences { BackupGenerations = 1 } };
        using var key = VaultKey.Derive(Secrets.MasterPassword, KdfParameters.CreateDefault());
        var backupPath = Path.Combine(other.Path, "old.dat");
        File.WriteAllBytes(backupPath, VaultCipher.Encrypt(key, VaultSerializer.Serialize(smallSetting)));

        var result = await lifecycle.RestoreAsync(token, backupPath, Secrets.MasterPassword);

        Assert.Equal(CommitStatus.Saved, result.Status);
        Assert.Equal(4, lifecycle.ListGenerationFiles().Count);
    }

    [Fact]
    public async Task Unlock_RemovesStaleEncryptedTempFiles_AndNothingElse()
    {
        using var lifecycle = CreateLifecycle();
        var s = await lifecycle.InitializeAsync(Secrets.MasterPassword);
        await lifecycle.CommitAsync(s.SessionToken, AddService("世代を作る"));
        await lifecycle.LockAsync();
        var store = new VaultFileStore(new PhysicalFileSystem(), new VaultPaths(_dir.Path), _clock);
        var preRestore = store.SavePreRestoreCopy();
        var generation = Assert.Single(store.ListGenerationFiles());
        var stale = Path.Combine(_dir.Path, "vault.dat.deadbeefdeadbeefdeadbeefdeadbeef.tmp");
        File.WriteAllBytes(stale, new byte[] { 1, 2, 3 });
        var uiSettings = Path.Combine(_dir.Path, "ui-settings.json");
        var uiSettingsTemp = Path.Combine(_dir.Path, "ui-settings.json.deadbeef.tmp");
        File.WriteAllText(uiSettings, "{\"theme\":\"sf\"}");
        File.WriteAllText(uiSettingsTemp, "{\"theme\":\"sf\"}");

        await lifecycle.UnlockAsync(Secrets.MasterPassword);

        Assert.False(File.Exists(stale));
        Assert.True(lifecycle.IsUnlocked);
        Assert.True(File.Exists(Path.Combine(_dir.Path, "vault.dat")));
        Assert.True(File.Exists(generation));
        Assert.True(File.Exists(preRestore));
        Assert.True(File.Exists(uiSettings));
        Assert.True(File.Exists(uiSettingsTemp));
        await lifecycle.LockAsync();
        Assert.Single((await lifecycle.UnlockAsync(Secrets.MasterPassword)).Document.Services);
    }

    [Fact]
    public async Task NoPlaintextSecret_EverTouchesTheDataDirectory()
    {
        using var lifecycle = CreateLifecycle();
        var s = await lifecycle.InitializeAsync(Secrets.MasterPassword);
        var withService = await lifecycle.CommitAsync(s.SessionToken, AddService(Secrets.ServiceName));
        var token = withService.Snapshot!.SessionToken;
        var withAccount = await lifecycle.CommitAsync(token, AddSentinelAccount(withService.CreatedId!.Value));
        Assert.Equal(CommitStatus.Saved, withAccount.Status);
        for (var i = 0; i < 3; i++)
        {
            await lifecycle.CommitAsync(token, AddService($"追加{i}"));
        }

        await lifecycle.ChangeMasterPasswordAsync(token, Secrets.OtherPassword);
        using var other = new TempDataDir();
        var manualBackup = Path.Combine(other.Path, "copy.dat");
        await lifecycle.CreateManualBackupAsync(token, manualBackup, overwrite: false);
        // 復元の経路（退避コピー・置き換え）も走査対象に含める
        var restored = await lifecycle.RestoreAsync(token, manualBackup, Secrets.OtherPassword);
        Assert.Equal(CommitStatus.Saved, restored.Status);
        await lifecycle.LockAsync();

        foreach (var secret in Secrets.All.Append(Secrets.MasterPassword).Append(Secrets.OtherPassword))
        {
            Assert.Empty(Secrets.FilesContaining(_dir.Path, secret));
        }

        Assert.Empty(Directory.GetFiles(_dir.Path, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Initialize_WhenVaultIsMissingButBackupsExist_IsRefused_AndKeepsThem()
    {
        using (var first = CreateLifecycle())
        {
            var s = await first.InitializeAsync(Secrets.MasterPassword);
            await first.CommitAsync(s.SessionToken, AddService("旧保管庫"));
        }

        File.Delete(Path.Combine(_dir.Path, "vault.dat"));
        var generations = Directory.GetFiles(Path.Combine(_dir.Path, "backups"), "vault-*.dat");
        Assert.NotEmpty(generations);

        using var second = CreateLifecycle();
        Assert.False(second.VaultExists);
        Assert.True(second.HasRecoverableFiles);
        var ex = await Assert.ThrowsAsync<VaultStorageException>(() => second.InitializeAsync(Secrets.OtherPassword));

        Assert.Equal(VaultStorageError.RecoverableFilesPresent, ex.Error);
        Assert.False(second.IsUnlocked);
        Assert.False(File.Exists(Path.Combine(_dir.Path, "vault.dat")));
        Assert.Equal(generations, Directory.GetFiles(Path.Combine(_dir.Path, "backups"), "vault-*.dat"));
    }

    [Fact]
    public async Task Initialize_WhenVaultIsMissingButAStaleTempFileExists_IsRefused_AndKeepsIt()
    {
        var stale = Path.Combine(_dir.Path, "vault.dat.deadbeefdeadbeefdeadbeefdeadbeef.tmp");
        File.WriteAllBytes(stale, new byte[] { 1, 2, 3 });
        using var lifecycle = CreateLifecycle();

        var ex = await Assert.ThrowsAsync<VaultStorageException>(() => lifecycle.InitializeAsync(Secrets.MasterPassword));

        Assert.Equal(VaultStorageError.RecoverableFilesPresent, ex.Error);
        Assert.True(File.Exists(stale));
        Assert.False(lifecycle.VaultExists);
    }

    [Fact]
    public async Task Commit_RevalidatesTheResultingDocument_AndRejectsAnInvalidOne()
    {
        using var lifecycle = CreateLifecycle();
        var s = await lifecycle.InitializeAsync(Secrets.MasterPassword);
        var duplicateId = Guid.NewGuid();
        var invalid = VaultDocument.Empty with { Services = new[] { Fx.Service("A", duplicateId), Fx.Service("B", duplicateId) } };

        var result = await lifecycle.CommitAsync(s.SessionToken, _ => OperationResult.Success(invalid, Array.Empty<ValidationIssue>()));

        Assert.Equal(CommitStatus.Rejected, result.Status);
        Assert.Contains(result.Issues, i => i.Code == IssueCode.DuplicateServiceId);
        Assert.Empty(lifecycle.Current!.Document.Services);
        await lifecycle.LockAsync();
        Assert.Empty((await lifecycle.UnlockAsync(Secrets.MasterPassword)).Document.Services);
    }

    [Fact]
    public async Task Unlock_WhenTheContentIsStructurallyBroken_FailsWithIntegrityError_AndStaysLocked()
    {
        var store = new VaultFileStore(new PhysicalFileSystem(), new VaultPaths(_dir.Path), _clock);
        using (var key = VaultKey.Derive(Secrets.MasterPassword, KdfParameters.CreateDefault()))
        {
            var broken = System.Text.Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"services\":null,\"accounts\":[null],\"preferences\":null}");
            store.WriteNew(VaultCipher.Encrypt(key, broken));
        }

        using var lifecycle = CreateLifecycle();
        await Assert.ThrowsAsync<VaultIntegrityException>(() => lifecycle.UnlockAsync(Secrets.MasterPassword));

        Assert.False(lifecycle.IsUnlocked);
        Assert.Null(lifecycle.Current);
    }

    [Fact]
    public async Task Restore_WhenReplaceFails_KeepsTheDocumentAndTheFile()
    {
        var fs = new FaultyFileSystem();
        using var lifecycle = CreateLifecycle(fs);
        var s = await lifecycle.InitializeAsync(Secrets.MasterPassword);
        var saved = await lifecycle.CommitAsync(s.SessionToken, AddService("現在"));
        var token = saved.Snapshot!.SessionToken;
        using var other = new TempDataDir();
        var backupPath = Path.Combine(other.Path, "backup.dat");
        await lifecycle.CreateManualBackupAsync(token, backupPath, overwrite: false);
        Assert.Equal(CommitStatus.Saved, (await lifecycle.CommitAsync(token, AddService("あとで追加"))).Status);
        var fileBefore = File.ReadAllBytes(Path.Combine(_dir.Path, "vault.dat"));
        fs.FailReplace = true;

        var result = await lifecycle.RestoreAsync(token, backupPath, Secrets.MasterPassword);

        Assert.Equal(CommitStatus.Failed, result.Status);
        Assert.Equal(2, lifecycle.Current!.Document.Services.Count);
        Assert.Equal(fileBefore, File.ReadAllBytes(Path.Combine(_dir.Path, "vault.dat")));
        Assert.Empty(Directory.GetFiles(_dir.Path, "*.tmp"));
    }

    [Fact]
    public async Task ManualBackup_WithStaleToken_Throws_AndWritesNothing()
    {
        using var lifecycle = CreateLifecycle();
        var s = await lifecycle.InitializeAsync(Secrets.MasterPassword);
        await lifecycle.LockAsync();
        using var other = new TempDataDir();
        var destination = Path.Combine(other.Path, "copy.dat");

        await Assert.ThrowsAsync<InvalidOperationException>(() => lifecycle.CreateManualBackupAsync(s.SessionToken, destination, overwrite: false));

        Assert.False(File.Exists(destination));
    }

    [Fact]
    public async Task Dispose_Twice_DoesNotThrow_AndLeavesTheVaultLocked()
    {
        var lifecycle = CreateLifecycle();
        await lifecycle.InitializeAsync(Secrets.MasterPassword);

        lifecycle.Dispose();
        lifecycle.Dispose();

        Assert.False(lifecycle.IsUnlocked);
    }
}
