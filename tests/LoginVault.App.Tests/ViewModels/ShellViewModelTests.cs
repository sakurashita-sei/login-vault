using System.IO;
using LoginVault.App.Clipboard;
using LoginVault.App.Locking;
using LoginVault.App.Tests.Support;
using LoginVault.App.ViewModels;
using LoginVault.Infrastructure.Session;
using LoginVault.Infrastructure.Storage;

namespace LoginVault.App.Tests.ViewModels;

public class ShellViewModelTests : IDisposable
{
    private const string Password = "テスト用マスターパスワード 架空 2026";
    private readonly TempDataDir _dir = new();
    private readonly FakeTime _time = new();
    private readonly FakeDialogService _dialogs = new();
    private readonly FakeClipboardDevice _device = new();

    public void Dispose() => _dir.Dispose();

    private ShellViewModel CreateShell(IVaultFileSystem? fs = null)
    {
        var lifecycle = new VaultLifecycle(new VaultFileStore(fs ?? new PhysicalFileSystem(), new VaultPaths(_dir.Path), _time), _time);
        var clipboard = new ClipboardGuard(_device, _time, _ => Task.CompletedTask);
        var lockController = new LockController(_time, _time);
        return new ShellViewModel(lifecycle, _dialogs, clipboard, lockController, _time, _time);
    }

    private async Task<ShellViewModel> CreateUnlockedShellAsync()
    {
        var shell = CreateShell();
        await shell.InitializeVaultAsync(Password, Password);
        Assert.Equal(ShellState.Unlocked, shell.State);
        return shell;
    }

    [Fact]
    public void EmptyDataDirectory_StartsInSetup()
    {
        var shell = CreateShell();

        Assert.Equal(ShellState.NeedsSetup, shell.State);
        Assert.Contains("復元できません", shell.SetupNotice, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExistingVault_StartsLocked()
    {
        var first = await CreateUnlockedShellAsync();
        await first.LockAsync(LockReason.Manual);

        var second = CreateShell();

        Assert.Equal(ShellState.Locked, second.State);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("abc", "abd")]
    public async Task Setup_EmptyOrMismatchedPassword_ShowsError_AndCreatesNothing(string password, string confirmation)
    {
        var shell = CreateShell();

        await shell.InitializeVaultAsync(password, confirmation);

        Assert.Equal(ShellState.NeedsSetup, shell.State);
        Assert.False(string.IsNullOrEmpty(shell.Message));
        Assert.False(File.Exists(Path.Combine(_dir.Path, "vault.dat")));
    }

    [Fact]
    public async Task Setup_RequiresConfirmation_AboutIrrecoverability()
    {
        var shell = CreateShell();
        _dialogs.ConfirmAnswers.Enqueue(false);

        await shell.InitializeVaultAsync(Password, Password);

        Assert.Equal(ShellState.NeedsSetup, shell.State);
        var confirm = Assert.Single(_dialogs.Confirms);
        Assert.Contains("復元できません", confirm.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(_dir.Path, "vault.dat")));
    }

    [Fact]
    public async Task Setup_WeakPassword_IsWarnedInConfirmation()
    {
        var shell = CreateShell();

        await shell.InitializeVaultAsync("short", "short");

        Assert.Equal(ShellState.Unlocked, shell.State);
        Assert.Contains("弱い", Assert.Single(_dialogs.Confirms).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Setup_PreservesWhitespaceInPassword()
    {
        var shell = CreateShell();
        await shell.InitializeVaultAsync(" " + Password + " ", " " + Password + " ");
        await shell.LockAsync(LockReason.Manual);

        await shell.UnlockAsync(Password);
        Assert.Equal(ShellState.Locked, shell.State);
        await shell.UnlockAsync(" " + Password + " ");
        Assert.Equal(ShellState.Unlocked, shell.State);
    }

    [Fact]
    public async Task Unlock_WrongPassword_ShowsErrorAndStaysLocked()
    {
        var shell = await CreateUnlockedShellAsync();
        await shell.LockAsync(LockReason.Manual);

        await shell.UnlockAsync("wrong");

        Assert.Equal(ShellState.Locked, shell.State);
        Assert.Contains("マスターパスワード", shell.Message, StringComparison.Ordinal);
        Assert.Null(shell.Vault);
    }

    [Theory]
    [InlineData(LockReason.Idle)]
    [InlineData(LockReason.SessionLocked)]
    [InlineData(LockReason.Suspend)]
    public async Task Lock_ByTimerOrSystem_ShowsNotice_NotError(LockReason reason)
    {
        var shell = await CreateUnlockedShellAsync();

        await shell.LockAsync(reason);

        Assert.False(string.IsNullOrEmpty(shell.Message));
        Assert.True(shell.IsMessageNotice);
    }

    [Fact]
    public async Task Unlock_WrongPassword_MessageIsError_NotNotice()
    {
        var shell = await CreateUnlockedShellAsync();
        await shell.LockAsync(LockReason.Idle);
        Assert.True(shell.IsMessageNotice);

        await shell.UnlockAsync("wrong password");

        Assert.False(string.IsNullOrEmpty(shell.Message));
        Assert.False(shell.IsMessageNotice);
    }

    [Fact]
    public async Task Lock_ClearsVault_ClosesDialogs_AndClearsOwnClipboardContent()
    {
        var shell = await CreateUnlockedShellAsync();
        _dialogs.TextAnswers.Enqueue("ゲームA");
        await shell.Vault!.AddServiceAsync();
        _dialogs.EditorHandler = editor =>
        {
            editor.LoginIdText = "player01";
            editor.SetNewPassword("game-pw");
            return true;
        };
        await shell.Vault.AddAccountAsync();
        await shell.Vault.CopyPasswordAsync(shell.Vault.Accounts.Single());
        Assert.Equal("game-pw", _device.CurrentText);

        await shell.LockAsync(LockReason.Manual);

        Assert.Equal(ShellState.Locked, shell.State);
        Assert.Null(shell.Vault);
        Assert.Equal(1, _dialogs.CloseAllCalls);
        Assert.Null(_device.Current);
        Assert.False(shell.LockController.IsRunning);
    }

    [Fact]
    public async Task IdleTimeout_LocksTheVault()
    {
        var shell = await CreateUnlockedShellAsync();

        _time.Advance(TimeSpan.FromMinutes(5));
        await shell.WaitForPendingLockAsync();

        Assert.Equal(ShellState.Locked, shell.State);
    }

    [Fact]
    public async Task Activity_PostponesIdleLock()
    {
        var shell = await CreateUnlockedShellAsync();

        _time.Advance(TimeSpan.FromMinutes(4));
        shell.NotifyActivity();
        _time.Advance(TimeSpan.FromMinutes(4));
        await shell.WaitForPendingLockAsync();

        Assert.Equal(ShellState.Unlocked, shell.State);
    }

    [Fact]
    public async Task SessionLockSignal_LocksTheVault()
    {
        var shell = await CreateUnlockedShellAsync();

        shell.LockController.NotifySessionLocked();
        await shell.WaitForPendingLockAsync();

        Assert.Equal(ShellState.Locked, shell.State);
    }

    [Fact]
    public async Task Deactivation_ConcealsRevealedPassword_ButDoesNotLock()
    {
        var shell = await CreateUnlockedShellAsync();
        _dialogs.TextAnswers.Enqueue("ゲームA");
        await shell.Vault!.AddServiceAsync();
        _dialogs.EditorHandler = editor =>
        {
            editor.LoginIdText = "player01";
            editor.SetNewPassword("game-pw");
            return true;
        };
        await shell.Vault.AddAccountAsync();
        var item = shell.Vault.Accounts.Single();
        shell.Vault.ToggleReveal(item);
        Assert.True(item.IsRevealed);

        shell.NotifyDeactivated();

        Assert.False(item.IsRevealed);
        Assert.Equal(ShellState.Unlocked, shell.State);
    }

    [Fact]
    public async Task Settings_ChangeIdleTimeout_IsAppliedToLockController()
    {
        var shell = await CreateUnlockedShellAsync();
        _dialogs.SettingsHandler = settings =>
        {
            settings.AutoLockMinutes = 1;
            return true;
        };

        await shell.Vault!.OpenSettingsAsync();

        Assert.Equal(TimeSpan.FromMinutes(1), shell.LockController.IdleTimeout);
        _time.Advance(TimeSpan.FromMinutes(1));
        await shell.WaitForPendingLockAsync();
        Assert.Equal(ShellState.Locked, shell.State);
    }

    [Fact]
    public async Task LockDuringEditorConfirmation_DoesNotReopenTheEditor()
    {
        var shell = await CreateUnlockedShellAsync();
        var vault = shell.Vault!;
        await vault.Lifecycle.CommitAsync(vault.Snapshot.SessionToken, d => LoginVault.Core.Operations.VaultOperations.AddService(d, "Google", _time.UtcNow));
        vault.RefreshFromLifecycle();
        var editorShown = 0;
        Task pendingLock = Task.CompletedTask;
        _dialogs.EditorHandler = editor =>
        {
            editorShown++;
            editor.SelectedService = editor.Services[0];
            editor.Label = "メイン";
            editor.LoginIdText = "main@example.com";
            // パスワードは空欄のまま → 警告の確認ダイアログが出る
            return true;
        };
        _dialogs.ConfirmHandler = _ =>
        {
            // 確認ダイアログが開いている間に無操作ロックが走り、ダイアログは「いいえ」で閉じられる
            pendingLock = shell.LockAsync(LockReason.Idle);
            return false;
        };

        await vault.AddAccountAsync();
        await pendingLock;

        Assert.Equal(1, editorShown);
        Assert.Equal(ShellState.Locked, shell.State);
    }

    [Fact]
    public async Task Lock_WhenClosingDialogsThrows_StillDiscardsTheKey_AndCanUnlockAgain()
    {
        var shell = await CreateUnlockedShellAsync();
        var lifecycle = shell.Vault!.Lifecycle;
        _dialogs.ThrowOnCloseAll = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => shell.LockAsync(LockReason.Manual));

        // 画面側の後始末で何が起きても、鍵の破棄とロック状態への遷移は必ず行われる
        Assert.False(lifecycle.IsUnlocked);
        Assert.Equal(ShellState.Locked, shell.State);
        Assert.Null(shell.Vault);
        Assert.False(shell.LockController.IsRunning);

        _dialogs.ThrowOnCloseAll = false;
        await shell.UnlockAsync(Password);
        Assert.Equal(ShellState.Unlocked, shell.State);
    }

    [Fact]
    public async Task SetupScreen_WhenOnlyBackupsRemain_ShowsTheRecoveryNoticeBeforeAnyInput()
    {
        var first = await CreateUnlockedShellAsync();
        await first.Vault!.Lifecycle.CommitAsync(first.Vault.Snapshot.SessionToken, d => LoginVault.Core.Operations.VaultOperations.AddService(d, "Google", _time.UtcNow));
        await first.LockAsync(LockReason.Manual);
        File.Delete(Path.Combine(_dir.Path, "vault.dat"));

        var shell = CreateShell();

        Assert.Equal(ShellState.NeedsSetup, shell.State);
        Assert.NotNull(shell.Message);
        Assert.Contains("backups", shell.Message, StringComparison.Ordinal);
        Assert.False(shell.IsMessageNotice);
    }

    [Fact]
    public async Task ScreenCaptureExclusion_FollowsTheVault_StaysOnWhileLocked_AndIsUnknownUntilTheNextUnlock()
    {
        var shell = await CreateUnlockedShellAsync();
        await shell.Vault!.ToggleScreenCaptureExclusionAsync();
        Assert.True(shell.IsScreenCaptureExcluded);

        // 同じセッション内ではロック画面も除外したままにする
        await shell.LockAsync(LockReason.Manual);
        Assert.True(shell.IsScreenCaptureExcluded);

        // 設定は保管庫の中にあるので、次回起動では解錠するまで分からない
        var restarted = CreateShell();
        Assert.False(restarted.IsScreenCaptureExcluded);
        await restarted.UnlockAsync(Password);
        Assert.True(restarted.IsScreenCaptureExcluded);
    }

    [Fact]
    public void WindowTitle_ShowsTheProductVersion()
    {
        var shell = CreateShell();

        Assert.Equal("Login Vault v" + LoginVault.App.Startup.AppVersion.Product, shell.WindowTitle);
    }
}
