using System.Runtime.ExceptionServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoginVault.App.Clipboard;
using LoginVault.App.Locking;
using LoginVault.App.Services;
using LoginVault.App.Startup;
using LoginVault.Core.Model;
using LoginVault.Core.Time;
using LoginVault.Infrastructure.Crypto;
using LoginVault.Infrastructure.Serialization;
using LoginVault.Infrastructure.Session;
using LoginVault.Infrastructure.Storage;

namespace LoginVault.App.ViewModels;

public enum ShellState
{
    NeedsSetup,
    Locked,
    Unlocked,
}

/// <summary>
/// Top-level state machine: first-time setup, locked, unlocked. Owns the lock controller wiring and
/// creates a fresh <see cref="VaultViewModel"/> per unlock session.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    public const int WeakPasswordLength = 12;

    public const string SetupNoticeText =
        "マスターパスワードを忘れると、この保管庫は復元できません。Login Vault はマスターパスワードを保存せず、復旧手段も用意していません。" +
        "必要であれば、アプリの外の安全な場所に控えてください。";

    private readonly VaultLifecycle _lifecycle;
    private readonly IDialogService _dialogs;
    private readonly ClipboardGuard _clipboard;
    private readonly ITimerScheduler _scheduler;
    private readonly IClock _clock;
    private Task _pendingLock = Task.CompletedTask;

    [ObservableProperty]
    private ShellState _state;

    [ObservableProperty]
    private VaultViewModel? _vault;

    [ObservableProperty]
    private string? _message;

    /// <summary>True when <see cref="Message"/> explains why the vault was locked (not an error).</summary>
    [ObservableProperty]
    private bool _isMessageNotice;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>
    /// True while the app's windows are hidden from screen capture. Follows the vault preference after unlock and keeps the
    /// last value while locked, so the lock screen of the same session stays hidden too. Unknown (false) until the first unlock.
    /// </summary>
    [ObservableProperty]
    private bool _isScreenCaptureExcluded;

    public ShellViewModel(
        VaultLifecycle lifecycle,
        IDialogService dialogs,
        ClipboardGuard clipboard,
        LockController lockController,
        ITimerScheduler scheduler,
        IClock clock)
    {
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        LockController = lockController ?? throw new ArgumentNullException(nameof(lockController));
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        State = _lifecycle.VaultExists ? ShellState.Locked : ShellState.NeedsSetup;
        if (State == ShellState.NeedsSetup && _lifecycle.HasRecoverableFiles)
        {
            // 旧保管庫の世代や保存途中のファイルが残っているときは、入力の前に復旧手順を示す（作成そのものは VaultLifecycle が拒否する）
            Message = VaultLifecycle.RecoverableFilesMessage;
        }

        LockController.LockRequested += reason => _pendingLock = LockAsync(reason);
        LockController.ConcealRequested += () => Vault?.ConcealAll();
        _clipboard.ClearAttempted += result => Vault?.ReportClipboardClear(result);
    }

    public LockController LockController { get; }

    public string SetupNotice => SetupNoticeText;

    /// <summary>Title bar text: product name and version (the same version the splash shows).</summary>
    public string WindowTitle => $"Login Vault v{AppVersion.Product}";

    /// <summary>Lets callers (and tests) await a lock that was triggered by a timer or Windows signal.</summary>
    public Task WaitForPendingLockAsync() => _pendingLock;

    public async Task InitializeVaultAsync(string password, string confirmation)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(confirmation);
        Message = null;
        IsMessageNotice = false;
        if (password.Length == 0)
        {
            Message = "マスターパスワードを入力してください。";
            return;
        }

        if (!string.Equals(password, confirmation, StringComparison.Ordinal))
        {
            Message = "確認用のマスターパスワードが一致しません。";
            return;
        }

        var text = SetupNoticeText;
        if (IsWeak(password))
        {
            text += Environment.NewLine + Environment.NewLine + $"入力されたマスターパスワードは{WeakPasswordLength}文字未満か空白のみで、弱い値です。推測されにくい長い値をおすすめします。";
        }

        text += Environment.NewLine + Environment.NewLine + "このマスターパスワードで保管庫を作成しますか？";
        if (!await _dialogs.ConfirmAsync("保管庫の作成", text, null, "作成する"))
        {
            return;
        }

        IsBusy = true;
        try
        {
            Open(await _lifecycle.InitializeAsync(password));
        }
        catch (VaultStorageException ex)
        {
            Message = ex.Message;
            State = _lifecycle.VaultExists ? ShellState.Locked : ShellState.NeedsSetup;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task UnlockAsync(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        Message = null;
        IsMessageNotice = false;
        if (password.Length == 0)
        {
            Message = "マスターパスワードを入力してください。";
            return;
        }

        IsBusy = true;
        try
        {
            Open(await _lifecycle.UnlockAsync(password));
        }
        catch (VaultAuthenticationException ex)
        {
            Message = ex.Message;
        }
        catch (VaultFormatException ex)
        {
            Message = "保管庫ファイルを読み込めません。" + ex.Message;
        }
        catch (VaultIntegrityException ex)
        {
            Message = "保管庫の内容に問題があるため開けません。" + ex.Message;
        }
        catch (VaultStorageException ex)
        {
            Message = ex.Message;
        }
        catch (InvalidOperationException ex)
        {
            Message = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task LockAsync(LockReason reason)
    {
        if (State != ShellState.Unlocked)
        {
            return;
        }

        // 画面側の後始末のどこで例外が出ても、鍵の破棄（_lifecycle.LockAsync）まで必ず進める。最初の例外は最後に投げ直す
        Exception? failure = null;
        void Attempt(Action step)
        {
            try
            {
                step();
            }
            catch (Exception ex)
            {
                failure ??= ex;
            }
        }

        var vault = Vault;
        Attempt(LockController.Stop);
        // ダイアログを閉じる前に「ロック中」にして、閉じられたダイアログから戻るループがエディターを再表示しないようにする
        Attempt(() => vault?.BeginLock());
        Attempt(_dialogs.CloseAll);
        Attempt(() => vault?.ConcealAll());
        Vault = null;
        State = ShellState.Locked;
        Message = reason switch
        {
            LockReason.Idle => "一定時間操作がなかったためロックしました。",
            LockReason.SessionLocked => "Windows のロックに合わせて保管庫をロックしました。",
            LockReason.Suspend => "スリープに合わせて保管庫をロックしました。",
            _ => null,
        };
        IsMessageNotice = Message is not null;
        Attempt(() => vault?.Dispose());
        try
        {
            // クリップボードの消去は、保存完了を待つ保管庫のロックより先に行う
            await _clipboard.ClearNowIfOwnAsync();
        }
        catch (Exception ex)
        {
            failure ??= ex;
        }

        await _lifecycle.LockAsync();
        if (failure is not null)
        {
            ExceptionDispatchInfo.Throw(failure);
        }
    }

    public Task LockManuallyAsync() => LockAsync(LockReason.Manual);

    /// <summary>Called by the window when it loses activation: conceal, do not lock.</summary>
    public void NotifyDeactivated() => LockController.NotifyDeactivated();

    public void NotifyActivity() => LockController.NotifyActivity();

    /// <summary>Applies saved preferences to the running lock controller.</summary>
    public void ApplyPreferences(VaultPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        LockController.IdleTimeout = TimeSpan.FromMinutes(preferences.AutoLockMinutes);
        IsScreenCaptureExcluded = preferences.ExcludeFromScreenCapture;
    }

    public static bool IsWeak(string password) => password.Length < WeakPasswordLength || string.IsNullOrWhiteSpace(password);

    private void Open(VaultSnapshot snapshot)
    {
        Vault = new VaultViewModel(_lifecycle, snapshot, _dialogs, _clipboard, _scheduler, _clock, LockAsync, ApplyPreferences);
        Message = null;
        IsMessageNotice = false;
        State = ShellState.Unlocked;
        ApplyPreferences(snapshot.Document.Preferences);
        LockController.Start();
    }
}
