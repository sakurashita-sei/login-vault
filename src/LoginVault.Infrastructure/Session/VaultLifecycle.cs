using System.Security.Cryptography;
using LoginVault.Core.Model;
using LoginVault.Core.Operations;
using LoginVault.Core.Time;
using LoginVault.Core.Validation;
using LoginVault.Infrastructure.Crypto;
using LoginVault.Infrastructure.Serialization;
using LoginVault.Infrastructure.Storage;

namespace LoginVault.Infrastructure.Session;

/// <summary>An immutable view of the unlocked vault bound to one unlock session.</summary>
public sealed record VaultSnapshot(Guid SessionToken, VaultDocument Document)
{
    public override string ToString() => $"VaultSnapshot({SessionToken})";
}

public enum CommitStatus
{
    /// <summary>Validated, encrypted and written.</summary>
    Saved,

    /// <summary>Validation errors; nothing was written.</summary>
    Rejected,

    /// <summary>The session token no longer matches (locked or re-unlocked since); nothing was written.</summary>
    StaleSession,

    /// <summary>Storage or decryption failure; the current vault file and document are unchanged.</summary>
    Failed,
}

public sealed class CommitResult
{
    private CommitResult(CommitStatus status, VaultSnapshot? snapshot, IReadOnlyList<ValidationIssue> issues, Exception? error, Guid? createdId)
    {
        Status = status;
        Snapshot = snapshot;
        Issues = issues;
        Error = error;
        CreatedId = createdId;
    }

    public CommitStatus Status { get; }

    public bool Succeeded => Status == CommitStatus.Saved;

    public VaultSnapshot? Snapshot { get; }

    /// <summary>Validation errors (Rejected) or new warnings (Saved).</summary>
    public IReadOnlyList<ValidationIssue> Issues { get; }

    public Exception? Error { get; }

    public Guid? CreatedId { get; }

    public static CommitResult Saved(VaultSnapshot snapshot, IReadOnlyList<ValidationIssue> warnings, Guid? createdId) =>
        new(CommitStatus.Saved, snapshot, warnings, null, createdId);

    public static CommitResult Rejected(IReadOnlyList<ValidationIssue> issues) =>
        new(CommitStatus.Rejected, null, issues, null, null);

    public static CommitResult Stale() =>
        new(CommitStatus.StaleSession, null, Array.Empty<ValidationIssue>(), null, null);

    public static CommitResult Failed(Exception error) =>
        new(CommitStatus.Failed, null, Array.Empty<ValidationIssue>(), error, null);
}

/// <summary>
/// Owns the unlocked state (derived key and current document) and serializes every unlock, commit,
/// lock, restore and master password change through one gate. Commits carry the session token
/// they were started for; a token from a locked or earlier session is refused, so work finishing
/// late can never overwrite a newer session, and a lock request waits for an in-flight save.
/// </summary>
public sealed class VaultLifecycle : IDisposable
{
    private readonly VaultFileStore _store;
    private readonly IClock _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private VaultKey? _key;
    private VaultDocument? _document;
    private Guid _token;
    private volatile VaultSnapshot? _current;

    public VaultLifecycle(VaultFileStore store, IClock clock)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public VaultPaths Paths => _store.Paths;

    public bool VaultExists => _store.Exists;

    /// <summary>True when the vault file is missing but backups or an interrupted save remain (the setup screen must not create a new vault over them).</summary>
    public bool HasRecoverableFiles => !_store.Exists && _store.HasRecoverableFiles;

    public bool IsUnlocked => _current is not null;

    /// <summary>The current session snapshot, or null while locked.</summary>
    public VaultSnapshot? Current => _current;

    /// <summary>Creates a new empty vault protected by the master password. Refuses to overwrite an existing vault.</summary>
    public async Task<VaultSnapshot> InitializeAsync(string masterPassword, CancellationToken cancellationToken = default)
    {
        RequirePassword(masterPassword);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_store.Exists)
            {
                throw new VaultStorageException(VaultStorageError.AlreadyExists, "保管庫は既に存在します。初期設定は行えません。");
            }

            if (_store.HasRecoverableFiles)
            {
                // 旧保管庫の世代や保存途中のファイルがあるのに新規作成すると、世代整理で旧保管庫のバックアップが消えていくため拒否する
                throw new VaultStorageException(VaultStorageError.RecoverableFilesPresent, RecoverableFilesMessage);
            }

            EnsureLocked();
            var key = await Task.Run(() => VaultKey.Derive(masterPassword, KdfParameters.CreateDefault()), cancellationToken).ConfigureAwait(false);
            try
            {
                var document = VaultDocument.Empty;
                await Task.Run(() => _store.WriteNew(EncryptDocument(key, document)), cancellationToken).ConfigureAwait(false);
                return Open(key, document);
            }
            catch
            {
                key.Dispose();
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Unlocks the existing vault. Throws typed exceptions for wrong password, tampering, unknown format or integrity errors; the file is never modified.</summary>
    public async Task<VaultSnapshot> UnlockAsync(string masterPassword, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(masterPassword);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureLocked();
            var (document, key) = await Task.Run(
                () =>
                {
                    var file = _store.Read();
                    var plaintext = VaultCipher.DecryptWithPassword(masterPassword, file, out var derived);
                    try
                    {
                        var deserialized = VaultSerializer.Deserialize(plaintext);
                        // 解錠に成功した時点で、異常終了が残した暗号化一時ファイルを掃除する
                        _store.DeleteStaleTempFiles();
                        return (deserialized, derived);
                    }
                    catch
                    {
                        derived.Dispose();
                        throw;
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(plaintext);
                    }
                },
                cancellationToken).ConfigureAwait(false);
            return Open(key, document);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Locks the vault. Waits for an in-flight save, then releases the key and the document.</summary>
    public async Task LockAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            CloseSession();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Applies a pure mutation to the current document and persists the result atomically.</summary>
    public async Task<CommitResult> CommitAsync(Guid sessionToken, Func<VaultDocument, OperationResult> mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!IsSession(sessionToken))
            {
                return CommitResult.Stale();
            }

            var result = mutation(_document!);
            if (!result.Succeeded)
            {
                return CommitResult.Rejected(result.Issues);
            }

            var key = _key!;
            var next = result.Document;
            // 操作側の検証結果を信用せず、保存直前にも文書全体を検証する（未検証の文書を書くと次の解錠で開けなくなるため）
            var report = VaultValidator.Validate(next);
            if (report.HasErrors)
            {
                return CommitResult.Rejected(report.Errors);
            }

            try
            {
                await Task.Run(() => _store.Replace(EncryptDocument(key, next), next.Preferences.BackupGenerations)).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsStorageFailure(ex))
            {
                return CommitResult.Failed(ex);
            }

            _document = next;
            _current = new VaultSnapshot(_token, next);
            return CommitResult.Saved(_current, result.Warnings, result.CreatedId);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Re-encrypts the vault with a new master password (new salt, new key). On failure the old key and file stay in effect.</summary>
    public async Task<CommitResult> ChangeMasterPasswordAsync(Guid sessionToken, string newMasterPassword)
    {
        RequirePassword(newMasterPassword);
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!IsSession(sessionToken))
            {
                return CommitResult.Stale();
            }

            var document = _document!;
            var newKey = await Task.Run(() => VaultKey.Derive(newMasterPassword, KdfParameters.CreateDefault())).ConfigureAwait(false);
            try
            {
                await Task.Run(() => _store.Replace(EncryptDocument(newKey, document), document.Preferences.BackupGenerations)).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsStorageFailure(ex))
            {
                newKey.Dispose();
                return CommitResult.Failed(ex);
            }
            catch
            {
                // 保存失敗以外の例外でも新しい鍵を残さない
                newKey.Dispose();
                throw;
            }

            _key!.Dispose();
            _key = newKey;
            _current = new VaultSnapshot(_token, document);
            return CommitResult.Saved(_current, Array.Empty<ValidationIssue>(), null);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Restores a whole document from a backup file: the backup is decrypted with its own password and
    /// validated first, the current vault is copied aside, then the restored document is re-encrypted
    /// with the current master password and written. No merge is performed.
    /// </summary>
    public async Task<CommitResult> RestoreAsync(Guid sessionToken, string backupFilePath, string backupPassword)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupFilePath);
        ArgumentNullException.ThrowIfNull(backupPassword);
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!IsSession(sessionToken))
            {
                return CommitResult.Stale();
            }

            var key = _key!;
            VaultDocument restored;
            try
            {
                restored = await Task.Run(
                    () =>
                    {
                        var bytes = _store.ReadExternal(backupFilePath);
                        var plaintext = VaultCipher.DecryptWithPassword(backupPassword, bytes, out var backupKey);
                        backupKey.Dispose();
                        try
                        {
                            return VaultSerializer.Deserialize(plaintext);
                        }
                        finally
                        {
                            CryptographicOperations.ZeroMemory(plaintext);
                        }
                    }).ConfigureAwait(false);

                // 世代整理は現在の設定と復元される設定の大きい方で行い、古い設定の復元で直前の世代を失わないようにする
                var generationsToKeep = Math.Max(_document!.Preferences.BackupGenerations, restored.Preferences.BackupGenerations);
                await Task.Run(
                    () =>
                    {
                        _store.SavePreRestoreCopy();
                        _store.Replace(EncryptDocument(key, restored), generationsToKeep);
                    }).ConfigureAwait(false);
            }
            catch (VaultIntegrityException ex)
            {
                return CommitResult.Rejected(ex.Issues);
            }
            catch (Exception ex) when (ex is VaultAuthenticationException or VaultFormatException || IsStorageFailure(ex))
            {
                return CommitResult.Failed(ex);
            }

            _document = restored;
            _current = new VaultSnapshot(_token, restored);
            return CommitResult.Saved(_current, Array.Empty<ValidationIssue>(), null);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Checks a password against the current session key (same salt and iterations) in constant time. Never touches the file.</summary>
    public async Task<bool> VerifyMasterPasswordAsync(Guid sessionToken, string candidatePassword)
    {
        ArgumentNullException.ThrowIfNull(candidatePassword);
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!IsSession(sessionToken))
            {
                return false;
            }

            var current = _key!;
            return await Task.Run(
                () =>
                {
                    using var candidate = VaultKey.Derive(candidatePassword, current.Kdf);
                    return CryptographicOperations.FixedTimeEquals(candidate.Bytes, current.Bytes);
                }).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Copies the encrypted vault file to a user-chosen path. Returns the full destination path.</summary>
    public async Task<string> CreateManualBackupAsync(Guid sessionToken, string destinationPath, bool overwrite)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!IsSession(sessionToken))
            {
                throw new InvalidOperationException("セッションが無効のためバックアップできません。");
            }

            return await Task.Run(() => _store.CopyTo(destinationPath, overwrite)).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public IReadOnlyList<string> ListGenerationFiles() => _store.ListGenerationFiles();

    public void Dispose()
    {
        // ゲートを取れた場合だけセッションを閉じる。セマフォは破棄しない（待機中の操作が残っていても完了でき、二重の Dispose も安全）
        if (_gate.Wait(TimeSpan.FromSeconds(10)))
        {
            try
            {
                CloseSession();
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    /// <summary>Shown when the vault file is missing but backups or an interrupted save remain (setup is refused; see <see cref="HasRecoverableFiles"/>).</summary>
    public const string RecoverableFilesMessage =
        "保管庫ファイル（vault.dat）が見つかりませんが、バックアップまたは保存途中のファイルがデータディレクトリに残っています。以前の保管庫を失わないよう、新しい保管庫は作成しません。" +
        "復旧するには、アプリを終了し、backups フォルダーで最も新しい vault-*.dat（保存途中のファイルがある場合は vault.dat.*.tmp）を vault.dat という名前にコピーしてデータディレクトリ直下へ置き、起動し直してください。" +
        "新しい保管庫を作りたい場合は、それらのファイルを別の場所へ移してください。";

    private static void RequirePassword(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        if (password.Length == 0)
        {
            throw new ArgumentException("マスターパスワードを入力してください。", nameof(password));
        }
    }

    private static bool IsStorageFailure(Exception ex) =>
        ex is VaultStorageException or IOException or UnauthorizedAccessException;

    private static byte[] EncryptDocument(VaultKey key, VaultDocument document)
    {
        // 平文はメモリ内だけで扱い、暗号化後に消去する
        var plaintext = VaultSerializer.Serialize(document);
        try
        {
            if (plaintext.Length > VaultFormat.MaxCiphertextLength)
            {
                // 形式の上限超えは保存失敗として報告する（呼び出し側は CommitResult.Failed に変換する）
                throw new VaultStorageException(VaultStorageError.WriteFailed, "保管庫の内容が大きすぎるため保存できません。");
            }

            return VaultCipher.Encrypt(key, plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private bool IsSession(Guid token) => _current is not null && token != Guid.Empty && token == _token;

    private void EnsureLocked()
    {
        if (_current is not null)
        {
            throw new InvalidOperationException("保管庫は既に解錠されています。");
        }
    }

    private VaultSnapshot Open(VaultKey key, VaultDocument document)
    {
        _key = key;
        _document = document;
        _token = Guid.NewGuid();
        _current = new VaultSnapshot(_token, document);
        return _current;
    }

    private void CloseSession()
    {
        _current = null;
        _token = Guid.Empty;
        _document = null;
        _key?.Dispose();
        _key = null;
    }
}
