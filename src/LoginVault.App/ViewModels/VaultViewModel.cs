using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoginVault.App.Clipboard;
using LoginVault.App.Locking;
using LoginVault.App.Services;
using LoginVault.Core.Model;
using LoginVault.Core.Operations;
using LoginVault.Core.Search;
using LoginVault.Core.Time;
using LoginVault.Core.Validation;
using LoginVault.Infrastructure.Session;
using LoginVault.Infrastructure.Storage;

namespace LoginVault.App.ViewModels;

/// <summary>
/// The unlocked main screen: service list (left), accounts (right), search, reveal/copy, and all
/// editing flows. Business rules live in Core; persistence in the lifecycle. This class only
/// coordinates dialogs, commits and display state.
/// </summary>
public sealed partial class VaultViewModel : ObservableObject, IDisposable
{
    public const string AllName = "すべて";
    public const string EmailOnlyName = "メールのみ";

    private readonly IDialogService _dialogs;
    private readonly ClipboardGuard _clipboard;
    private readonly ITimerScheduler _scheduler;
    private readonly IClock _clock;
    private readonly Func<LockReason, Task> _requestLock;
    private readonly Action<VaultPreferences> _preferencesChanged;
    private IDisposable? _revealTimer;
    private bool _refreshing;
    private bool _closing;

    [ObservableProperty]
    private ServiceListItem? _selectedServiceItem;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private AccountItemViewModel? _selectedAccount;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isSaving;

    [ObservableProperty]
    private string _emptyStateText = string.Empty;

    public VaultViewModel(
        VaultLifecycle lifecycle,
        VaultSnapshot snapshot,
        IDialogService dialogs,
        ClipboardGuard clipboard,
        ITimerScheduler scheduler,
        IClock clock,
        Func<LockReason, Task> requestLock,
        Action<VaultPreferences> preferencesChanged)
    {
        Lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _requestLock = requestLock ?? throw new ArgumentNullException(nameof(requestLock));
        _preferencesChanged = preferencesChanged ?? throw new ArgumentNullException(nameof(preferencesChanged));
        Refresh();
    }

    public VaultLifecycle Lifecycle { get; }

    public VaultSnapshot Snapshot { get; private set; }

    public ObservableCollection<ServiceListItem> ServiceItems { get; } = new();

    public ObservableCollection<AccountItemViewModel> Accounts { get; } = new();

    public bool ShowEmptyState => Accounts.Count == 0;

    public bool HasSelectedService => SelectedServiceItem?.ServiceId is not null;

    public string SelectedHeading => SelectedServiceItem?.Name ?? AllName;

    public string DataDirectory => Lifecycle.Paths.DataDirectory;

    /// <summary>Whether the app's windows are hidden from screen capture (a vault preference, shown as a check in the View menu).</summary>
    public bool IsScreenCaptureExcluded => Document.Preferences.ExcludeFromScreenCapture;

    private VaultDocument Document => Snapshot.Document;

    /// <summary>True while this view model still represents the lifecycle's current unlock session.</summary>
    private bool IsCurrentSession => !_closing && Lifecycle.Current is { } current && current.SessionToken == Snapshot.SessionToken;

    /// <summary>
    /// Called by the shell before it closes dialogs for a lock. Dialog-driven loops that resume afterwards
    /// (the closed confirmation returns "no") must not reopen an editor on top of the lock screen.
    /// </summary>
    public void BeginLock() => _closing = true;

    /// <summary>Re-reads the lifecycle snapshot (used after out-of-band commits, e.g. in tests).</summary>
    public void RefreshFromLifecycle()
    {
        Snapshot = Lifecycle.Current ?? Snapshot;
        Refresh();
    }

    public void ConcealAll()
    {
        _revealTimer?.Dispose();
        _revealTimer = null;
        foreach (var account in Accounts)
        {
            account.IsRevealed = false;
        }
    }

    /// <summary>Shows the outcome of a timed clipboard clear. Never includes the copied value.</summary>
    public void ReportClipboardClear(ClipboardClearResult result)
    {
        switch (result)
        {
            case ClipboardClearResult.Cleared:
                StatusMessage = "コピーした内容をクリップボードから消去しました。";
                break;
            case ClipboardClearResult.Busy:
            case ClipboardClearResult.Failed:
                StatusMessage = "クリップボードの自動消去ができませんでした（他のアプリが使用中など）。別の内容をコピーして上書きしてください。";
                break;
            default:
                break;
        }
    }

    [RelayCommand]
    public void ToggleReveal(AccountItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        if (item.IsRevealed)
        {
            ConcealAll();
            return;
        }

        ConcealAll();
        item.IsRevealed = true;
        _revealTimer = _scheduler.Schedule(TimeSpan.FromSeconds(Document.Preferences.RevealSeconds), () =>
        {
            item.IsRevealed = false;
            _revealTimer = null;
        });
    }

    [RelayCommand]
    public Task CopyPasswordAsync(AccountItemViewModel? item) =>
        item is null ? Task.CompletedTask : CopyAsync(item.Password, "パスワード");

    [RelayCommand]
    public Task CopyIdAsync(AccountItemViewModel? item)
    {
        if (item is null)
        {
            return Task.CompletedTask;
        }

        if (item.LoginId is null)
        {
            StatusMessage = "参照先のメールアカウントが見つからないため、IDをコピーできません。";
            return Task.CompletedTask;
        }

        return CopyAsync(item.LoginId, "ID");
    }

    [RelayCommand]
    public Task LockAsync() => _requestLock(LockReason.Manual);

    [RelayCommand]
    public async Task AddServiceAsync()
    {
        var name = await _dialogs.PromptTextAsync("サービスの追加", "サービス名（Google、Amazon、ゲーム名など）を入力してください。", null);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var trimmed = name.Trim();
        var existing = Document.Services.FirstOrDefault(s => string.Equals(s.Name, trimmed, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            var createAnyway = await _dialogs.ConfirmAsync(
                "同名のサービス",
                $"同じ名前のサービス「{existing.Name}」が既にあります。既存のサービスにアカウントを追加することもできます。{Environment.NewLine}{Environment.NewLine}それでも新しいサービスを作成しますか？",
                null,
                "新しく作成する");
            if (!createAnyway)
            {
                SelectService(existing.Id);
                return;
            }
        }

        var result = await CommitAsync(d => VaultOperations.AddService(d, trimmed, _clock.UtcNow));
        if (result?.Succeeded == true && result.CreatedId is { } id)
        {
            SelectService(id);
            StatusMessage = $"サービス「{trimmed}」を追加しました。";
        }
    }

    [RelayCommand]
    public async Task RenameServiceAsync()
    {
        if (SelectedServiceItem?.ServiceId is not { } serviceId || VaultQueries.FindService(Document, serviceId) is not { } service)
        {
            return;
        }

        var name = await _dialogs.PromptTextAsync("サービス名の変更", "新しいサービス名を入力してください。", service.Name);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var trimmed = name.Trim();
        if (string.Equals(trimmed, service.Name, StringComparison.Ordinal))
        {
            return;
        }

        var now = _clock.UtcNow;
        if (!await PreviewAndConfirmAsync(VaultOperations.RenameService(Document, serviceId, trimmed, now)))
        {
            return;
        }

        await CommitAsync(d => VaultOperations.RenameService(d, serviceId, trimmed, now));
    }

    [RelayCommand]
    public async Task DeleteServiceAsync()
    {
        if (SelectedServiceItem?.ServiceId is not { } serviceId || VaultQueries.FindService(Document, serviceId) is not { } service)
        {
            return;
        }

        var accounts = VaultQueries.AccountsOf(Document, serviceId);
        var internalIds = accounts.Select(a => a.Id).ToHashSet();
        var externalReferrers = Document.Accounts
            .Where(a => a.ServiceId != serviceId && a.LoginEmailAccountId is { } target && internalIds.Contains(target))
            .ToList();

        if (externalReferrers.Count > 0)
        {
            var proceed = await _dialogs.ConfirmAsync(
                "参照されているサービスの削除",
                $"サービス「{service.Name}」内のメールアカウントを、他のサービスの{externalReferrers.Count}件のアカウントがログインIDとして参照しています。{Environment.NewLine}" +
                $"参照しているアカウントを現在のアドレスの直接入力に変換してから、サービスと配下の{accounts.Count}件のアカウントを削除しますか？この操作は元に戻せません。",
                externalReferrers.Select(DescribeAccount).ToList(),
                "変換して削除",
                isDestructive: true);
            if (!proceed)
            {
                return;
            }

            var steps = externalReferrers
                .Select(r => (Func<VaultDocument, OperationResult>)(d => VaultOperations.ConvertReferenceToDirect(d, r.Id, _clock.UtcNow)))
                .Append(d => VaultOperations.DeleteService(d, serviceId))
                .ToArray();
            await CommitAsync(d => VaultOperations.ApplyAll(d, steps));
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            "サービスの削除",
            $"サービス「{service.Name}」と配下の{accounts.Count}件のアカウントを削除しますか？この操作は元に戻せません。",
            null,
            "削除",
            isDestructive: true);
        if (confirmed)
        {
            await CommitAsync(d => VaultOperations.DeleteService(d, serviceId));
        }
    }

    [RelayCommand]
    public async Task AddAccountAsync()
    {
        // サービスが 1 件もなくても開く（エディターで新しいサービスを入力して、アカウントとまとめて登録できる。D-048）
        var editor = new AccountEditorViewModel(Document, null, SelectedServiceItem?.ServiceId);
        editor.GeneratePasswordHandler = () => ShowPasswordGeneratorAsync(editor);
        // 入力エラー・警告の取り消し・保存失敗のときは、入力内容を保持したままエディターへ戻す。
        // ロック（ダイアログが閉じられる）後は再表示しない
        while (IsCurrentSession && await _dialogs.ShowAccountEditorAsync(editor))
        {
            if (!editor.TryBuildInput(out var input, out var error))
            {
                editor.ErrorMessage = error;
                continue;
            }

            var now = _clock.UtcNow;
            var add = WithTargetService(editor, input!, now, (d, serviceId) => VaultOperations.AddAccount(d, input! with { ServiceId = serviceId }, now));
            if (!await PreviewAndConfirmAsync(add(Document), editor))
            {
                continue;
            }

            if (!IsCurrentSession)
            {
                return;
            }

            // 新しいサービス名が既存のサービスと同じなら、そのサービスに追加する（保存前に判定して通知に使う）
            var reused = editor.IsNewService ? VaultQueries.FindServiceByName(Document, editor.NewServiceName) : null;
            var result = await CommitAsync(WithGeneratorOptions(editor, add), editor);
            if (result is null)
            {
                return;
            }

            if (result.Succeeded)
            {
                var created = VaultQueries.FindAccount(Document, result.CreatedId ?? Guid.Empty);
                SelectService(created?.ServiceId ?? input!.ServiceId);
                SelectedAccount = Accounts.FirstOrDefault(a => a.Id == result.CreatedId);
                StatusMessage = !editor.IsNewService
                    ? "アカウントを追加しました。"
                    : reused is not null
                        ? $"既存のサービス「{reused.Name}」にアカウントを追加しました。"
                        : $"サービス「{editor.NewServiceName.Trim()}」とアカウントを追加しました。";
                return;
            }
        }
    }

    [RelayCommand]
    public async Task EditAccountAsync(AccountItemViewModel? item)
    {
        if (item is null || VaultQueries.FindAccount(Document, item.Id) is not { } existing)
        {
            return;
        }

        var editor = new AccountEditorViewModel(Document, existing, null);
        editor.GeneratePasswordHandler = () => ShowPasswordGeneratorAsync(editor);
        while (IsCurrentSession && await _dialogs.ShowAccountEditorAsync(editor))
        {
            if (!editor.TryBuildInput(out var input, out var error))
            {
                editor.ErrorMessage = error;
                continue;
            }

            var now = _clock.UtcNow;
            var addressChanged = existing.IsEmailAccount && existing.LoginIdMode == LoginIdMode.Direct
                && input!.IsEmailAccount && input.LoginIdMode == LoginIdMode.Direct
                && !string.Equals(existing.LoginIdText, input.LoginIdText, StringComparison.Ordinal);
            var referrers = addressChanged ? VaultQueries.ReferrersOf(Document, existing.Id) : Array.Empty<AccountEntry>();

            // 所属サービスの変更先が新しいサービスなら、同じ保存でサービスも登録する（D-048）
            var update = WithTargetService(editor, input!, now, (d, serviceId) => VaultOperations.UpdateAccount(d, existing.Id, input! with { ServiceId = serviceId }, now));
            Func<VaultDocument, OperationResult> mutation;
            if (referrers.Count > 0)
            {
                var impact = new EmailChangeImpactViewModel(Document, existing.LoginIdText ?? string.Empty, input!.LoginIdText ?? string.Empty, referrers);
                if (!await _dialogs.ShowEmailChangeImpactAsync(impact))
                {
                    continue;
                }

                if (!IsCurrentSession)
                {
                    return;
                }

                var steps = impact.Referrers
                    .Where(r => r.ConvertToDirect)
                    .Select(r => (Func<VaultDocument, OperationResult>)(d => VaultOperations.ConvertReferenceToDirect(d, r.AccountId, now)))
                    .Append(update)
                    .ToArray();
                mutation = d => VaultOperations.ApplyAll(d, steps);
            }
            else
            {
                mutation = update;
            }

            if (!await PreviewAndConfirmAsync(mutation(Document), editor))
            {
                continue;
            }

            if (!IsCurrentSession)
            {
                return;
            }

            var result = await CommitAsync(WithGeneratorOptions(editor, mutation), editor);
            if (result is null)
            {
                return;
            }

            if (result.Succeeded)
            {
                if (editor.IsNewService && VaultQueries.FindAccount(Document, existing.Id) is { } moved)
                {
                    // 新しいサービスへ移したときは、移った先を一覧で選ぶ
                    SelectService(moved.ServiceId);
                    SelectedAccount = Accounts.FirstOrDefault(a => a.Id == moved.Id);
                }

                StatusMessage = "アカウントを保存しました。";
                return;
            }
        }
    }

    [RelayCommand]
    public async Task DeleteAccountAsync(AccountItemViewModel? item)
    {
        if (item is null || VaultQueries.FindAccount(Document, item.Id) is not { } existing)
        {
            return;
        }

        var referrers = VaultQueries.ReferrersOf(Document, existing.Id);
        if (referrers.Count > 0)
        {
            var proceed = await _dialogs.ConfirmAsync(
                "参照されているメールアカウントの削除",
                $"「{DescribeAccount(existing)}」は{referrers.Count}件のアカウントからログインIDとして参照されています。{Environment.NewLine}" +
                $"参照しているアカウントを現在のアドレス（{existing.LoginIdText}）の直接入力に変換してから削除しますか？この操作は元に戻せません。",
                referrers.Select(DescribeAccount).ToList(),
                "変換して削除",
                isDestructive: true);
            if (!proceed)
            {
                return;
            }

            var steps = referrers
                .Select(r => (Func<VaultDocument, OperationResult>)(d => VaultOperations.ConvertReferenceToDirect(d, r.Id, _clock.UtcNow)))
                .Append(d => VaultOperations.DeleteAccount(d, existing.Id))
                .ToArray();
            await CommitAsync(d => VaultOperations.ApplyAll(d, steps));
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            "アカウントの削除",
            $"「{DescribeAccount(existing)}」を削除しますか？この操作は元に戻せません。",
            null,
            "削除",
            isDestructive: true);
        if (confirmed)
        {
            await CommitAsync(d => VaultOperations.DeleteAccount(d, existing.Id));
        }
    }

    [RelayCommand]
    public async Task OpenSettingsAsync()
    {
        var settings = new SettingsViewModel(Document.Preferences, DataDirectory);
        if (!await _dialogs.ShowSettingsAsync(settings))
        {
            return;
        }

        if (!settings.TryBuild(out var preferences, out var error))
        {
            await _dialogs.ShowMessageAsync("設定を保存できません", error ?? string.Empty);
            return;
        }

        var result = await CommitAsync(d => VaultOperations.UpdatePreferences(d, preferences!));
        if (result?.Succeeded == true)
        {
            _preferencesChanged(preferences!);
            StatusMessage = "設定を保存しました。";
        }
    }

    /// <summary>Turns screen-capture exclusion on or off, saving the choice inside the vault and applying it through the shell.</summary>
    [RelayCommand]
    public async Task ToggleScreenCaptureExclusionAsync()
    {
        var enable = !IsScreenCaptureExcluded;
        var result = await CommitAsync(d => VaultOperations.UpdatePreferences(d, d.Preferences with { ExcludeFromScreenCapture = enable }));
        if (result?.Succeeded == true)
        {
            _preferencesChanged(Document.Preferences);
            StatusMessage = enable
                ? "画面キャプチャから除外しました。画面共有・録画・スクリーンショットにこのアプリのウィンドウは写りません。"
                : "画面キャプチャの除外を解除しました。";
        }

        // 成功・失敗にかかわらず、メニューのチェック表示を保管庫の値に合わせ直す
        OnPropertyChanged(nameof(IsScreenCaptureExcluded));
    }

    [RelayCommand]
    public async Task ChangeMasterPasswordAsync()
    {
        if (IsSaving)
        {
            StatusMessage = "保存中です。完了までお待ちください。";
            return;
        }

        var request = await _dialogs.ShowChangeMasterPasswordAsync();
        if (request is null)
        {
            return;
        }

        if (request.NewPassword.Length == 0)
        {
            await _dialogs.ShowMessageAsync("マスターパスワードの変更", "新しいマスターパスワードを入力してください。");
            return;
        }

        if (!await Lifecycle.VerifyMasterPasswordAsync(Snapshot.SessionToken, request.CurrentPassword))
        {
            if (IsCurrentSession)
            {
                await _dialogs.ShowMessageAsync("マスターパスワードの変更", "現在のマスターパスワードが違います。");
            }

            return;
        }

        if (ShellViewModel.IsWeak(request.NewPassword))
        {
            var proceed = await _dialogs.ConfirmAsync(
                "弱いマスターパスワード",
                $"新しいマスターパスワードは{ShellViewModel.WeakPasswordLength}文字未満か空白のみで、弱い値です。このまま変更しますか？",
                null,
                "変更する");
            if (!proceed)
            {
                return;
            }
        }

        IsSaving = true;
        try
        {
            var result = await Lifecycle.ChangeMasterPasswordAsync(Snapshot.SessionToken, request.NewPassword);
            switch (result.Status)
            {
                case CommitStatus.Saved:
                    Snapshot = result.Snapshot!;
                    if (IsCurrentSession)
                    {
                        await _dialogs.ShowMessageAsync(
                            "マスターパスワードを変更しました",
                            "保管庫を新しいマスターパスワードで暗号化し直しました。" + Environment.NewLine +
                            "以前に作成された旧世代のバックアップや手動コピーは旧マスターパスワードのまま残っています。必要に応じて新しいバックアップを作成し、古いものを整理してください。");
                    }

                    break;
                case CommitStatus.Failed:
                    ErrorMessage = "マスターパスワードの変更に失敗しました。保管庫は変更前のまま保持されています。" + result.Error?.Message;
                    break;
                default:
                    break;
            }
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    public async Task CreateManualBackupAsync()
    {
        var suggested = $"LoginVault-backup-{_clock.UtcNow.ToLocalTime():yyyyMMdd-HHmm}.dat";
        var destination = await _dialogs.PickBackupDestinationAsync(suggested);
        if (string.IsNullOrWhiteSpace(destination))
        {
            return;
        }

        try
        {
            var path = await Lifecycle.CreateManualBackupAsync(Snapshot.SessionToken, destination, overwrite: true);
            StatusMessage = "バックアップを作成しました。";
            if (IsCurrentSession)
            {
                await _dialogs.ShowMessageAsync(
                    "バックアップを作成しました",
                    "暗号化された保管庫ファイルを次の場所にコピーしました。" + Environment.NewLine + path + Environment.NewLine + Environment.NewLine +
                    "バックアップは作成時点のマスターパスワードで暗号化されています。削除済み・変更前の情報もバックアップには残ります。USB メモリーなど別の媒体へ定期的にコピーし、実際に開けることを確認してください。");
            }
        }
        catch (VaultStorageException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    public async Task RestoreAsync()
    {
        if (IsSaving)
        {
            StatusMessage = "保存中です。完了までお待ちください。";
            return;
        }

        var request = await _dialogs.ShowRestoreAsync();
        if (request is null)
        {
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            "バックアップからの復元",
            "現在の保管庫は退避コピーを保存したうえで、選択したバックアップの内容で全体を置き換えます（マージはしません）。続行しますか？",
            null,
            "復元する",
            isDestructive: true);
        if (!confirmed)
        {
            return;
        }

        IsSaving = true;
        try
        {
            var preferencesBefore = Document.Preferences;
            var result = await Lifecycle.RestoreAsync(Snapshot.SessionToken, request.FilePath, request.Password);
            if (result.Status == CommitStatus.Saved)
            {
                Snapshot = result.Snapshot!;
                Refresh();
                // 復元した文書の設定（無操作ロック時間など）を動作中の制御にも反映する
                _preferencesChanged(Snapshot.Document.Preferences);
                StatusMessage = "バックアップから復元しました。";
            }

            if (!IsCurrentSession)
            {
                return;
            }

            switch (result.Status)
            {
                case CommitStatus.Saved:
                    await _dialogs.ShowMessageAsync(
                        "復元しました",
                        "バックアップの内容で保管庫を置き換えました。復元前の保管庫は backups フォルダー内に pre-restore- で始まる名前で退避されています。"
                        + DescribePreferenceChanges(preferencesBefore, Snapshot.Document.Preferences));
                    break;
                case CommitStatus.Rejected:
                    await _dialogs.ShowMessageAsync("復元できません", "バックアップの内容に整合性エラーがあるため復元しませんでした。", result.Issues.Select(i => i.Message).Distinct().ToList());
                    break;
                case CommitStatus.Failed:
                    await _dialogs.ShowMessageAsync("復元できません", result.Error?.Message ?? "復元に失敗しました。");
                    break;
                default:
                    break;
            }
        }
        finally
        {
            IsSaving = false;
        }
    }

    public void Dispose()
    {
        _closing = true;
        _revealTimer?.Dispose();
        _revealTimer = null;
    }

    /// <summary>Shows the password generator for an editor, starting from the saved options (or the ones chosen earlier in this editor). Changed options are kept on the editor and saved with the account.</summary>
    private async Task<string?> ShowPasswordGeneratorAsync(AccountEditorViewModel editor)
    {
        var generator = new PasswordGeneratorViewModel(editor.GeneratorOptionsToSave ?? Document.Preferences.PasswordGenerator);
        if (!await _dialogs.ShowPasswordGeneratorAsync(generator) || !generator.CanAccept)
        {
            return null;
        }

        var options = generator.ToOptions();
        editor.GeneratorOptionsToSave = options == Document.Preferences.PasswordGenerator ? null : options;
        return generator.Candidate;
    }

    /// <summary>Applies an account operation to the service chosen in the editor; a new service is resolved (an existing one with the same name is reused) or added in the same result (D-048).</summary>
    private static Func<VaultDocument, OperationResult> WithTargetService(AccountEditorViewModel editor, AccountInput input, DateTimeOffset now, Func<VaultDocument, Guid, OperationResult> apply)
    {
        if (!editor.IsNewService)
        {
            return d => apply(d, input.ServiceId);
        }

        var serviceName = editor.NewServiceName;
        return d => VaultOperations.WithService(d, serviceName, now, apply);
    }

    /// <summary>Adds the generator options chosen in the editor to the same save as the account (one commit, one generation).</summary>
    private static Func<VaultDocument, OperationResult> WithGeneratorOptions(AccountEditorViewModel editor, Func<VaultDocument, OperationResult> mutation)
    {
        if (editor.GeneratorOptionsToSave is not { } options)
        {
            return mutation;
        }

        return d => VaultOperations.ApplyAll(
            d,
            step => VaultOperations.UpdatePreferences(step, step.Preferences with { PasswordGenerator = options }),
            mutation);
    }

    /// <summary>Lists the preference values a restore changed, so laxer settings carried by a backup do not take effect unnoticed.</summary>
    internal static string DescribePreferenceChanges(VaultPreferences before, VaultPreferences after)
    {
        var changes = new List<string>();
        if (before.AutoLockMinutes != after.AutoLockMinutes)
        {
            changes.Add($"無操作ロック {before.AutoLockMinutes} 分 → {after.AutoLockMinutes} 分");
        }

        if (before.RevealSeconds != after.RevealSeconds)
        {
            changes.Add($"パスワード表示時間 {before.RevealSeconds} 秒 → {after.RevealSeconds} 秒");
        }

        if (before.ClipboardClearSeconds != after.ClipboardClearSeconds)
        {
            changes.Add($"コピー消去 {before.ClipboardClearSeconds} 秒 → {after.ClipboardClearSeconds} 秒");
        }

        if (before.BackupGenerations != after.BackupGenerations)
        {
            changes.Add($"自動バックアップの世代数 {before.BackupGenerations} → {after.BackupGenerations}");
        }

        if (before.ExcludeFromScreenCapture != after.ExcludeFromScreenCapture)
        {
            changes.Add($"画面キャプチャから除外 {OnOff(before.ExcludeFromScreenCapture)} → {OnOff(after.ExcludeFromScreenCapture)}");
        }

        static string OnOff(bool value) => value ? "オン" : "オフ";

        if (changes.Count == 0)
        {
            return string.Empty;
        }

        return Environment.NewLine + Environment.NewLine + "バックアップに含まれていた設定も適用されました: " + string.Join("、", changes) + "。必要なら「設定」で見直してください。";
    }

    private async Task CopyAsync(string value, string label)
    {
        var seconds = Document.Preferences.ClipboardClearSeconds;
        var outcome = await _clipboard.CopyAsync(value, TimeSpan.FromSeconds(seconds));
        if (outcome == CopyOutcome.Superseded)
        {
            // Busy 待ちの間に新しいコピーかロックに追い越された古い要求。新しい方の状態表示を上書きしない
            return;
        }

        StatusMessage = outcome switch
        {
            CopyOutcome.Copied => $"{label}をコピーしました（{seconds}秒後に自動で消去します）。",
            CopyOutcome.Busy => $"クリップボードが他のアプリで使用中のため、{label}をコピーできませんでした。",
            _ => $"{label}をコピーできませんでした。",
        };
    }

    /// <summary>
    /// Shows validation errors (inline in the editor when one is given, otherwise as a dialog) and asks about
    /// new warnings such as an empty password or a duplicate login ID. Returns true to continue with the save.
    /// </summary>
    private async Task<bool> PreviewAndConfirmAsync(OperationResult preview, AccountEditorViewModel? editor = null)
    {
        if (!preview.Succeeded)
        {
            var details = ReferrerDetails(preview.Errors);
            if (editor is not null)
            {
                var text = IssueText.Describe(preview.Errors);
                if (details is { Count: > 0 })
                {
                    text += Environment.NewLine + string.Join(Environment.NewLine, details.Select(d => "　- " + d));
                }

                editor.ShowError(text, preview.Errors);
            }
            else
            {
                await _dialogs.ShowMessageAsync("保存できません", IssueText.Describe(preview.Errors), details);
            }

            return false;
        }

        if (preview.Warnings.Count == 0)
        {
            return true;
        }

        return await _dialogs.ConfirmAsync(
            "確認",
            IssueText.Describe(preview.Warnings) + Environment.NewLine + Environment.NewLine + "このまま保存しますか？",
            null,
            "保存する");
    }

    private IReadOnlyList<string>? ReferrerDetails(IEnumerable<ValidationIssue> errors)
    {
        var referenced = errors.FirstOrDefault(e => e.Code == IssueCode.AccountIsReferenced)?.AccountId;
        if (referenced is not { } id)
        {
            return null;
        }

        return VaultQueries.ReferrersOf(Document, id).Select(DescribeAccount).ToList();
    }

    /// <summary>
    /// Persists a mutation through the lifecycle. On failure the previous document stays in effect; when an
    /// editor is given, the reason is written to it so the user can retry without losing input.
    /// Returns null when the session is no longer current (locked meanwhile) or another save is in progress.
    /// </summary>
    private async Task<CommitResult?> CommitAsync(Func<VaultDocument, OperationResult> mutation, AccountEditorViewModel? editor = null)
    {
        if (IsSaving)
        {
            StatusMessage = "保存中です。完了までお待ちください。";
            return null;
        }

        IsSaving = true;
        ErrorMessage = null;
        try
        {
            var result = await Lifecycle.CommitAsync(Snapshot.SessionToken, mutation);
            switch (result.Status)
            {
                case CommitStatus.Saved:
                    Snapshot = result.Snapshot!;
                    Refresh();
                    return result;
                case CommitStatus.Rejected:
                    if (editor is not null)
                    {
                        editor.ShowError(IssueText.Describe(result.Issues), result.Issues);
                    }
                    else if (IsCurrentSession)
                    {
                        await _dialogs.ShowMessageAsync("保存できません", IssueText.Describe(result.Issues), ReferrerDetails(result.Issues));
                    }

                    return result;
                case CommitStatus.Failed:
                    ErrorMessage = "保存に失敗しました。直前に保存した内容は保持されています。" + result.Error?.Message;
                    if (editor is not null)
                    {
                        editor.ErrorMessage = "保存に失敗しました。入力内容は保持されています。もう一度「保存」を押すか、キャンセルしてください。" + Environment.NewLine + result.Error?.Message;
                    }

                    return result;
                default:
                    // ロック済みまたは別セッション: 画面は既に切り替わっているため何もしない
                    return null;
            }
        }
        finally
        {
            IsSaving = false;
        }
    }

    private string DescribeAccount(AccountEntry account)
    {
        var serviceName = VaultQueries.FindService(Document, account.ServiceId)?.Name ?? "（不明なサービス）";
        return $"{serviceName}／{VaultQueries.DisplayName(Document, account)}";
    }

    private void SelectService(Guid serviceId)
    {
        var item = ServiceItems.FirstOrDefault(i => i.ServiceId == serviceId);
        if (item is not null)
        {
            SelectedServiceItem = item;
        }
    }

    private void Refresh()
    {
        var wasRefreshing = _refreshing;
        _refreshing = true;
        try
        {
            var document = Document;
            var all = SearchEngine.Search(document, SearchText, AccountFilter.All);
            var emailOnly = SearchEngine.Search(document, SearchText, AccountFilter.EmailOnly);
            var previousKind = SelectedServiceItem?.Kind ?? ServiceListItemKind.All;
            var previousId = SelectedServiceItem?.ServiceId;

            ServiceItems.Clear();
            ServiceItems.Add(new ServiceListItem(ServiceListItemKind.All, AllName, null, all.Accounts.Count));
            ServiceItems.Add(new ServiceListItem(ServiceListItemKind.EmailOnly, EmailOnlyName, null, emailOnly.Accounts.Count));
            // サービス別の件数は一度だけ数える（サービス数×アカウント数の走査にしない）
            var countsByService = all.Accounts.GroupBy(a => a.ServiceId).ToDictionary(g => g.Key, g => g.Count());
            foreach (var service in all.Services)
            {
                ServiceItems.Add(new ServiceListItem(ServiceListItemKind.Service, service.Name, service.Id, countsByService.GetValueOrDefault(service.Id)));
            }

            SelectedServiceItem = ServiceItems.FirstOrDefault(i => i.Kind == previousKind && i.ServiceId == previousId) ?? ServiceItems[0];
            RefreshAccounts(all, emailOnly);
            OnPropertyChanged(nameof(IsScreenCaptureExcluded));
        }
        finally
        {
            _refreshing = wasRefreshing;
        }
    }

    private void RefreshAccounts(SearchResult all, SearchResult emailOnly)
    {
        var wasRefreshing = _refreshing;
        _refreshing = true;
        try
        {
            ConcealAll();
            var document = Document;
            var selected = SelectedServiceItem;
            IEnumerable<AccountEntry> source = selected?.Kind switch
            {
                ServiceListItemKind.EmailOnly => emailOnly.Accounts,
                ServiceListItemKind.Service => all.Accounts.Where(a => a.ServiceId == selected.ServiceId),
                _ => all.Accounts,
            };
            var previousAccountId = SelectedAccount?.Id;
            Accounts.Clear();
            foreach (var account in source)
            {
                Accounts.Add(new AccountItemViewModel(account, document));
            }

            SelectedAccount = Accounts.FirstOrDefault(a => a.Id == previousAccountId);
            EmptyStateText = ComputeEmptyStateText(document, selected, all);
            OnPropertyChanged(nameof(ShowEmptyState));
            OnPropertyChanged(nameof(HasSelectedService));
            OnPropertyChanged(nameof(SelectedHeading));
        }
        finally
        {
            _refreshing = wasRefreshing;
        }
    }

    private string ComputeEmptyStateText(VaultDocument document, ServiceListItem? selected, SearchResult all)
    {
        if (document.Services.Count == 0)
        {
            return "サービスがまだありません。右上の「＋追加」でアカウントと一緒にサービスを登録するか、左下の「＋サービス」から Google やゲーム名などのサービスを追加してください。";
        }

        if (!string.IsNullOrWhiteSpace(SearchText) && all.Accounts.Count == 0)
        {
            return "検索条件に一致するアカウントはありません。";
        }

        return selected?.Kind switch
        {
            ServiceListItemKind.EmailOnly => "メールアカウントとして登録された情報はありません。アカウントの編集で「この情報はメールアカウントのログイン情報ですか？」を「はい」にすると、ここに表示されます。",
            ServiceListItemKind.Service => "このサービスにはアカウントがまだありません。右上の「＋追加」から登録してください。",
            _ => string.IsNullOrWhiteSpace(SearchText)
                ? "アカウントがまだありません。サービスを選んで「＋追加」から登録してください。"
                : "検索条件に一致するアカウントはありません。",
        };
    }

    partial void OnSelectedServiceItemChanged(ServiceListItem? value)
    {
        if (_refreshing)
        {
            return;
        }

        var all = SearchEngine.Search(Document, SearchText, AccountFilter.All);
        var emailOnly = SearchEngine.Search(Document, SearchText, AccountFilter.EmailOnly);
        RefreshAccounts(all, emailOnly);
    }

    partial void OnSearchTextChanged(string value)
    {
        if (!_refreshing)
        {
            Refresh();
        }
    }

    partial void OnSelectedAccountChanged(AccountItemViewModel? value)
    {
        if (!_refreshing)
        {
            ConcealAll();
        }
    }
}
