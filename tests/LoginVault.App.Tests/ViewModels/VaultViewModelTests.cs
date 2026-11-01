using System.IO;
using LoginVault.App.Clipboard;
using LoginVault.App.Locking;
using LoginVault.App.Services;
using LoginVault.App.Tests.Support;
using LoginVault.App.ViewModels;
using LoginVault.Core.Model;
using LoginVault.Core.Operations;
using LoginVault.Infrastructure.Session;
using LoginVault.Infrastructure.Storage;

namespace LoginVault.App.Tests.ViewModels;

public class VaultViewModelTests : IDisposable
{
    private const string Password = "テスト用マスターパスワード 架空 2026";
    private readonly TempDataDir _dir = new();
    private readonly FakeTime _time = new();
    private readonly FakeDialogService _dialogs = new();
    private readonly FakeClipboardDevice _device = new();
    private readonly FaultyFileSystemForApp _fs = new();

    public void Dispose() => _dir.Dispose();

    private async Task<(ShellViewModel Shell, VaultViewModel Vault)> CreateAsync(Func<TimeSpan, Task>? clipboardDelay = null)
    {
        var lifecycle = new VaultLifecycle(new VaultFileStore(_fs, new VaultPaths(_dir.Path), _time), _time);
        var clipboard = new ClipboardGuard(_device, _time, clipboardDelay ?? (_ => Task.CompletedTask));
        var shell = new ShellViewModel(lifecycle, _dialogs, clipboard, new LockController(_time, _time), _time, _time);
        await shell.InitializeVaultAsync(Password, Password);
        // 初回設定時の確認ダイアログは各テストの対象外なので記録を消す
        _dialogs.Confirms.Clear();
        return (shell, shell.Vault!);
    }

    /// <summary>Seeds Google(メイン, サブ: email accounts), Amazon(個人用 → Google/メイン), ゲームA(メイン) through the lifecycle.</summary>
    private static async Task SeedScenarioAsync(VaultViewModel vault)
    {
        var token = vault.Snapshot.SessionToken;
        var lifecycle = vault.Lifecycle;
        var google = await lifecycle.CommitAsync(token, d => VaultOperations.AddService(d, "Google", DateTimeOffset.UtcNow));
        var amazon = await lifecycle.CommitAsync(token, d => VaultOperations.AddService(d, "Amazon", DateTimeOffset.UtcNow));
        var game = await lifecycle.CommitAsync(token, d => VaultOperations.AddService(d, "ゲームA", DateTimeOffset.UtcNow));
        var main = await lifecycle.CommitAsync(token, d => VaultOperations.AddAccount(d, new AccountInput { ServiceId = google.CreatedId!.Value, Label = "メイン", LoginIdText = "main@example.com", Password = PasswordChange.Set("google-main-pw"), IsEmailAccount = true }, DateTimeOffset.UtcNow));
        await lifecycle.CommitAsync(token, d => VaultOperations.AddAccount(d, new AccountInput { ServiceId = google.CreatedId!.Value, Label = "サブ", LoginIdText = "sub@example.com", Password = PasswordChange.Set("google-sub-pw"), IsEmailAccount = true }, DateTimeOffset.UtcNow));
        await lifecycle.CommitAsync(token, d => VaultOperations.AddAccount(d, new AccountInput { ServiceId = amazon.CreatedId!.Value, Label = "個人用", LoginIdMode = LoginIdMode.EmailReference, LoginEmailAccountId = main.CreatedId!.Value, Password = PasswordChange.Set("amazon-pw"), Notes = "プライム" }, DateTimeOffset.UtcNow));
        await lifecycle.CommitAsync(token, d => VaultOperations.AddAccount(d, new AccountInput { ServiceId = game.CreatedId!.Value, Label = "メイン", LoginIdText = "player01", Password = PasswordChange.Set("game-pw") }, DateTimeOffset.UtcNow));
        vault.RefreshFromLifecycle();
    }

    private static ServiceListItem ServiceItem(VaultViewModel vault, string name) => vault.ServiceItems.Single(s => s.Name == name && s.ServiceId is not null);

    [Fact]
    public async Task EmptyVault_ShowsEmptyState()
    {
        var (_, vault) = await CreateAsync();

        Assert.True(vault.ShowEmptyState);
        Assert.Contains("サービス", vault.EmptyStateText, StringComparison.Ordinal);
        Assert.Equal(2, vault.ServiceItems.Count);
    }

    [Fact]
    public async Task AddService_PromptsForName_AndSelectsNewService()
    {
        var (_, vault) = await CreateAsync();
        _dialogs.TextAnswers.Enqueue("  Google ");

        await vault.AddServiceAsync();

        var item = ServiceItem(vault, "Google");
        Assert.Equal(0, item.Count);
        Assert.Same(item, vault.SelectedServiceItem);
        Assert.Contains("アカウント", vault.EmptyStateText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddService_DuplicateName_AsksAndSelectsExistingWhenDeclined()
    {
        var (_, vault) = await CreateAsync();
        _dialogs.TextAnswers.Enqueue("Google");
        await vault.AddServiceAsync();
        _dialogs.TextAnswers.Enqueue("google");
        _dialogs.ConfirmAnswers.Enqueue(false);

        await vault.AddServiceAsync();

        Assert.Single(vault.ServiceItems, s => s.ServiceId is not null);
        Assert.Contains("同じ名前", Assert.Single(_dialogs.Confirms).Message, StringComparison.Ordinal);
        Assert.Equal("Google", vault.SelectedServiceItem?.Name);
    }

    [Fact]
    public async Task AddAccount_ThroughEditor_AppearsInListAndIsPersisted()
    {
        var (_, vault) = await CreateAsync();
        _dialogs.TextAnswers.Enqueue("ゲームA");
        await vault.AddServiceAsync();
        _dialogs.EditorHandler = editor =>
        {
            editor.Label = "メイン";
            editor.LoginIdText = "player01";
            editor.SetNewPassword(" game pw ");
            return true;
        };

        await vault.AddAccountAsync();

        var item = Assert.Single(vault.Accounts);
        Assert.Equal("メイン", item.DisplayName);
        Assert.Equal("player01", item.LoginIdDisplay);
        Assert.Equal(1, ServiceItem(vault, "ゲームA").Count);
        Assert.Equal(" game pw ", vault.Snapshot.Document.Accounts.Single().Password);
    }

    [Fact]
    public async Task AddAccount_EmptyPassword_AsksForConfirmation_AndReturnsToEditorWhenDeclined()
    {
        var (_, vault) = await CreateAsync();
        _dialogs.TextAnswers.Enqueue("ゲームA");
        await vault.AddServiceAsync();
        var calls = 0;
        AccountEditorViewModel? reopened = null;
        _dialogs.EditorHandler = editor =>
        {
            calls++;
            if (calls == 1)
            {
                editor.LoginIdText = "player01";
                return true;
            }

            reopened = editor;
            return false;
        };
        _dialogs.ConfirmAnswers.Enqueue(false);

        await vault.AddAccountAsync();

        Assert.Empty(vault.Accounts);
        Assert.Contains("パスワードが空欄", Assert.Single(_dialogs.Confirms).Message, StringComparison.Ordinal);
        Assert.Equal(2, calls);
        Assert.Equal("player01", reopened!.LoginIdText);
    }

    [Fact]
    public async Task AddAccount_SaveFailure_ReopensEditorWithEnteredValuesAndError()
    {
        var (_, vault) = await CreateAsync();
        _dialogs.TextAnswers.Enqueue("ゲームA");
        await vault.AddServiceAsync();
        _fs.FailReplace = true;
        var calls = 0;
        AccountEditorViewModel? reopened = null;
        _dialogs.EditorHandler = editor =>
        {
            calls++;
            if (calls == 1)
            {
                editor.Label = "メイン";
                editor.LoginIdText = "player01";
                editor.SetNewPassword("game-pw");
                return true;
            }

            reopened = editor;
            return false;
        };

        await vault.AddAccountAsync();

        Assert.Equal(2, calls);
        Assert.Equal("メイン", reopened!.Label);
        Assert.Equal("player01", reopened.LoginIdText);
        Assert.Equal("game-pw", reopened.PendingNewPassword);
        Assert.Contains("保存", reopened.ErrorMessage, StringComparison.Ordinal);
        Assert.Empty(vault.Accounts);
        Assert.False(vault.IsSaving);
    }

    [Fact]
    public async Task EditAccount_RejectedChange_ReopensEditorWithError()
    {
        var (_, vault) = await CreateAsync();
        await SeedScenarioAsync(vault);
        vault.SelectedServiceItem = ServiceItem(vault, "Google");
        var calls = 0;
        AccountEditorViewModel? reopened = null;
        _dialogs.EditorHandler = editor =>
        {
            calls++;
            if (calls == 1)
            {
                editor.IsEmailAccount = false;
                return true;
            }

            reopened = editor;
            return false;
        };

        await vault.EditAccountAsync(vault.Accounts.Single(a => a.DisplayName == "メイン"));

        Assert.Equal(2, calls);
        Assert.Contains("参照", reopened!.ErrorMessage, StringComparison.Ordinal);
        // メールアカウントのフラグは詳細設定の中にあるため、再表示では詳細設定が開いている
        Assert.True(reopened.IsDetailsExpanded);
        Assert.True(vault.Snapshot.Document.Accounts.Single(a => a.IsEmailAccount && a.Label == "メイン").IsEmailAccount);
    }

    [Fact]
    public async Task RenameService_DuplicateName_AsksForConfirmation()
    {
        var (_, vault) = await CreateAsync();
        await SeedScenarioAsync(vault);
        vault.SelectedServiceItem = ServiceItem(vault, "ゲームA");
        _dialogs.TextAnswers.Enqueue("google");
        _dialogs.ConfirmAnswers.Enqueue(false);

        await vault.RenameServiceAsync();

        Assert.Contains("同じ名前", Assert.Single(_dialogs.Confirms).Message, StringComparison.Ordinal);
        Assert.Contains(vault.ServiceItems, s => s.Name == "ゲームA");
        _dialogs.TextAnswers.Enqueue("google");
        _dialogs.ConfirmAnswers.Enqueue(true);
        await vault.RenameServiceAsync();
        Assert.DoesNotContain(vault.ServiceItems, s => s.Name == "ゲームA");
        Assert.Equal(2, vault.ServiceItems.Count(s => string.Equals(s.Name, "google", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task ClipboardAutoClear_IsReportedInStatus_WithoutTheValue()
    {
        var (_, vault) = await CreateAsync();
        await SeedScenarioAsync(vault);
        vault.SelectedServiceItem = ServiceItem(vault, "Amazon");
        await vault.CopyPasswordAsync(vault.Accounts.Single());

        _time.Advance(TimeSpan.FromSeconds(30));

        Assert.Null(_device.Current);
        Assert.Contains("消去", vault.StatusMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("amazon-pw", vault.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClipboardAutoClearFailure_IsReportedInStatus()
    {
        var (_, vault) = await CreateAsync();
        await SeedScenarioAsync(vault);
        vault.SelectedServiceItem = ServiceItem(vault, "Amazon");
        await vault.CopyPasswordAsync(vault.Accounts.Single());
        _device.BusyClearCount = 100;

        _time.Advance(TimeSpan.FromSeconds(30));

        Assert.Contains("消去", vault.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("できませんでした", vault.StatusMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("amazon-pw", vault.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditorCancelled_ChangesNothing()
    {
        var (_, vault) = await CreateAsync();
        _dialogs.TextAnswers.Enqueue("ゲームA");
        await vault.AddServiceAsync();
        _dialogs.EditorHandler = _ => false;

        await vault.AddAccountAsync();

        Assert.Empty(vault.Accounts);
    }

    [Fact]
    public async Task SelectingService_ShowsItsAccounts_WithResolvedIds()
    {
        var (_, vault) = await CreateAsync();
        await SeedScenarioAsync(vault);

        vault.SelectedServiceItem = ServiceItem(vault, "Amazon");

        var item = Assert.Single(vault.Accounts);
        Assert.Equal("個人用", item.DisplayName);
        Assert.Equal("main@example.com", item.LoginIdDisplay);
        Assert.True(item.IsReference);
        Assert.Contains("Google", item.ReferenceSourceText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_FiltersServicesAndAccounts()
    {
        var (_, vault) = await CreateAsync();
        await SeedScenarioAsync(vault);

        vault.SearchText = "サブ";

        Assert.Single(vault.ServiceItems, s => s.ServiceId is not null);
        Assert.Equal("サブ", Assert.Single(vault.Accounts).DisplayName);
    }

    [Fact]
    public async Task Search_NoHits_ShowsEmptySearchState()
    {
        var (_, vault) = await CreateAsync();
        await SeedScenarioAsync(vault);

        vault.SearchText = "存在しない";

        Assert.Empty(vault.Accounts);
        Assert.True(vault.ShowEmptyState);
        Assert.Contains("検索", vault.EmptyStateText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmailOnlyFilter_ShowsOnlyEmailAccounts()
    {
        var (_, vault) = await CreateAsync();
        await SeedScenarioAsync(vault);

        vault.SelectedServiceItem = vault.ServiceItems.Single(s => s.Kind == ServiceListItemKind.EmailOnly);

        Assert.Equal(2, vault.Accounts.Count);
        Assert.All(vault.Accounts, a => Assert.True(a.IsEmailAccount));
    }

    [Fact]
    public async Task Reveal_ShowsOnlyOneAccount_AndHidesAfterTimeout()
    {
        var (_, vault) = await CreateAsync();
        await SeedScenarioAsync(vault);
        vault.SelectedServiceItem = ServiceItem(vault, "Google");
        var first = vault.Accounts[0];
        var second = vault.Accounts[1];
        Assert.Equal(AccountItemViewModel.Mask, first.PasswordDisplay);

        vault.ToggleReveal(first);
        Assert.True(first.IsRevealed);
        Assert.Equal("google-main-pw", first.PasswordDisplay);

        vault.ToggleReveal(second);
        Assert.False(first.IsRevealed);
        Assert.True(second.IsRevealed);

        _time.Advance(TimeSpan.FromSeconds(15));
        Assert.False(second.IsRevealed);
        Assert.Equal(AccountItemViewModel.Mask, second.PasswordDisplay);
    }

    [Fact]
    public async Task Reveal_IsHiddenWhenSelectionChanges_OrToggledAgain()
    {
        var (_, vault) = await CreateAsync();
        await SeedScenarioAsync(vault);
        vault.SelectedServiceItem = ServiceItem(vault, "Google");
        var item = vault.Accounts[0];

        vault.ToggleReveal(item);
        vault.ToggleReveal(item);
        Assert.False(item.IsRevealed);

        vault.ToggleReveal(item);
        vault.SelectedAccount = vault.Accounts[1];
        Assert.False(item.IsRevealed);
    }

    [Fact]
    public async Task MaskHasFixedLength_RegardlessOfPassword()
    {
        var (_, vault) = await CreateAsync();
        await SeedScenarioAsync(vault);
        vault.SelectedServiceItem = ServiceItem(vault, "Google");

        Assert.All(vault.Accounts, a => Assert.Equal(AccountItemViewModel.Mask, a.PasswordDisplay));
        Assert.DoesNotContain("google", AccountItemViewModel.Mask, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AccountItemViewModel_MfaText_DisplaysNotConfigured_ForUnverifiedAndNotConfigured()
    {
        var doc = new VaultDocument();
        var time = new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.FromHours(9));
        var sId = Guid.NewGuid();

        var unverified = new AccountEntry { Id = Guid.NewGuid(), ServiceId = sId, LoginIdMode = LoginIdMode.Direct, MfaStatus = MfaStatus.Unverified, CreatedAt = time, UpdatedAt = time };
        var notConfigured = new AccountEntry { Id = Guid.NewGuid(), ServiceId = sId, LoginIdMode = LoginIdMode.Direct, MfaStatus = MfaStatus.NotConfigured, CreatedAt = time, UpdatedAt = time };
        var enabled = new AccountEntry { Id = Guid.NewGuid(), ServiceId = sId, LoginIdMode = LoginIdMode.Direct, MfaStatus = MfaStatus.Enabled, MfaMethods = new[] { new MfaMethod(MfaMethodType.Sms, Detail: "1234") }, CreatedAt = time, UpdatedAt = time };

        var itemUnverified = new AccountItemViewModel(unverified, doc);
        var itemNotConfigured = new AccountItemViewModel(notConfigured, doc);
        var itemEnabled = new AccountItemViewModel(enabled, doc);

        Assert.Equal("二段階認証：未設定", itemUnverified.MfaText);
        Assert.Equal("二段階認証：未設定", itemNotConfigured.MfaText);
        Assert.Equal("二段階認証：設定あり（SMS：1234）", itemEnabled.MfaText);
    }

    [Fact]
    public async Task CopyPassword_WorksWhileMasked_AndStatusNeverContainsTheValue()
    {
        var (_, vault) = await CreateAsync();
        await SeedScenarioAsync(vault);
        vault.SelectedServiceItem = ServiceItem(vault, "Amazon");
        var item = vault.Accounts.Single();

        await vault.CopyPasswordAsync(item);

        Assert.False(item.IsRevealed);
        Assert.Equal("amazon-pw", _device.CurrentText);
        Assert.Contains("コピー", vault.StatusMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("amazon-pw", vault.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CopyId_CopiesResolvedAddressForReferences()
    {
        var (_, vault) = await CreateAsync();
        await SeedScenarioAsync(vault);
        vault.SelectedServiceItem = ServiceItem(vault, "Amazon");

        await vault.CopyIdAsync(vault.Accounts.Single());

        Assert.Equal("main@example.com", _device.CurrentText);
    }

    [Fact]
    public async Task Copy_WhenClipboardBusy_ReportsFailureWithoutValue()
    {
        var (_, vault) = await CreateAsync();
        await SeedScenarioAsync(vault);
        vault.SelectedServiceItem = ServiceItem(vault, "Amazon");
        _device.BusySetCount = 100;

        await vault.CopyPasswordAsync(vault.Accounts.Single());

        Assert.Null(_device.Current);
        Assert.Contains("できませんでした", vault.StatusMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("amazon-pw", vault.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeleteAccount_ConfirmDeclined_KeepsAccount()
    {
        var (_, vault) = await CreateAsync();
        await SeedScenarioAsync(vault);
        vault.SelectedServiceItem = ServiceItem(vault, "ゲームA");
        _dialogs.ConfirmAnswers.Enqueue(false);

        await vault.DeleteAccountAsync(vault.Accounts.Single());

        Assert.Single(vault.Accounts);
    }

    [Fact]
    public async Task DeleteReferencedEmail_OffersConversion_AndDeletesInOneSave()
    {
        var (_, vault) = await CreateAsync();
        await SeedScenarioAsync(vault);
        vault.SelectedServiceItem = ServiceItem(vault, "Google");
        var main = vault.Accounts.Single(a => a.DisplayName == "メイン");

        await vault.DeleteAccountAsync(main);

        var confirm = Assert.Single(_dialogs.Confirms);
        Assert.Contains("直接入力", confirm.Message, StringComparison.Ordinal);
        Assert.Contains(confirm.Details!, d => d.Contains("Amazon", StringComparison.Ordinal));
        Assert.Single(vault.Accounts);
        var amazon = vault.Snapshot.Document.Accounts.Single(a => a.Label == "個人用");
        Assert.Equal(LoginIdMode.Direct, amazon.LoginIdMode);
        Assert.Equal("main@example.com", amazon.LoginIdText);
    }

    [Fact]
    public async Task DeleteReferencedEmail_Declined_ChangesNothing()
    {
        var (_, vault) = await CreateAsync();
        await SeedScenarioAsync(vault);
        vault.SelectedServiceItem = ServiceItem(vault, "Google");
        _dialogs.ConfirmAnswers.Enqueue(false);

        await vault.DeleteAccountAsync(vault.Accounts.Single(a => a.DisplayName == "メイン"));

        Assert.Equal(2, vault.Accounts.Count);
        Assert.Equal(LoginIdMode.EmailReference, vault.Snapshot.Document.Accounts.Single(a => a.Label == "個人用").LoginIdMode);
    }

    [Fact]
    public async Task DeleteService_WithExternalReferences_OffersConversion()
    {
        var (_, vault) = await CreateAsync();
        await SeedScenarioAsync(vault);
        vault.SelectedServiceItem = ServiceItem(vault, "Google");

        await vault.DeleteServiceAsync();

        Assert.Contains("直接入力", Assert.Single(_dialogs.Confirms).Message, StringComparison.Ordinal);
        Assert.DoesNotContain(vault.ServiceItems, s => s.Name == "Google");
        Assert.Equal("main@example.com", vault.Snapshot.Document.Accounts.Single(a => a.Label == "個人用").LoginIdText);
    }

    [Fact]
    public async Task DeleteService_WithoutReferences_ConfirmsWithCount()
    {
        var (_, vault) = await CreateAsync();
        await SeedScenarioAsync(vault);
        vault.SelectedServiceItem = ServiceItem(vault, "ゲームA");

        await vault.DeleteServiceAsync();

        Assert.Contains("1件", Assert.Single(_dialogs.Confirms).Message, StringComparison.Ordinal);
        Assert.DoesNotContain(vault.ServiceItems, s => s.Name == "ゲームA");
    }

    [Fact]
    public async Task EditEmailAddress_WithReferrers_ShowsImpact_AndConvertsSelectedReferrer()
    {
        var (_, vault) = await CreateAsync();
        await SeedScenarioAsync(vault);
        vault.SelectedServiceItem = ServiceItem(vault, "Google");
        _dialogs.EditorHandler = editor =>
        {
            editor.LoginIdText = "renamed@example.com";
            return true;
        };
        _dialogs.ImpactHandler = impact =>
        {
            Assert.Equal("main@example.com", impact.OldAddress);
            Assert.Equal("renamed@example.com", impact.NewAddress);
            Assert.Contains("自動では変更されません", impact.WarningText, StringComparison.Ordinal);
            var referrer = Assert.Single(impact.Referrers);
            referrer.ConvertToDirect = true;
            return true;
        };

        await vault.EditAccountAsync(vault.Accounts.Single(a => a.DisplayName == "メイン"));

        var doc = vault.Snapshot.Document;
        Assert.Equal("renamed@example.com", doc.Accounts.Single(a => a.IsEmailAccount && a.Label == "メイン").LoginIdText);
        var amazon = doc.Accounts.Single(a => a.Label == "個人用");
        Assert.Equal(LoginIdMode.Direct, amazon.LoginIdMode);
        Assert.Equal("main@example.com", amazon.LoginIdText);
    }

    [Fact]
    public async Task EditEmailAddress_WithReferrers_NoConversion_PropagatesAddress()
    {
        var (_, vault) = await CreateAsync();
        await SeedScenarioAsync(vault);
        vault.SelectedServiceItem = ServiceItem(vault, "Google");
        _dialogs.EditorHandler = editor =>
        {
            editor.LoginIdText = "renamed@example.com";
            return true;
        };
        _dialogs.ImpactHandler = _ => true;

        await vault.EditAccountAsync(vault.Accounts.Single(a => a.DisplayName == "メイン"));

        var amazon = vault.Snapshot.Document.Accounts.Single(a => a.Label == "個人用");
        Assert.Equal(LoginIdMode.EmailReference, amazon.LoginIdMode);
        Assert.Equal("renamed@example.com", VaultQueries.ResolveLoginId(vault.Snapshot.Document, amazon).Value);
    }

    [Fact]
    public async Task EditEmailAddress_ImpactDialogDeclined_ReturnsToEditor_AndChangesNothingWhenCancelled()
    {
        var (_, vault) = await CreateAsync();
        await SeedScenarioAsync(vault);
        vault.SelectedServiceItem = ServiceItem(vault, "Google");
        var calls = 0;
        _dialogs.EditorHandler = editor =>
        {
            calls++;
            if (calls > 1)
            {
                return false;
            }

            editor.LoginIdText = "renamed@example.com";
            return true;
        };
        _dialogs.ImpactHandler = _ => false;

        await vault.EditAccountAsync(vault.Accounts.Single(a => a.DisplayName == "メイン"));

        Assert.Equal("main@example.com", vault.Snapshot.Document.Accounts.Single(a => a.IsEmailAccount && a.Label == "メイン").LoginIdText);
    }

    [Fact]
    public async Task EditAccount_KeepPassword_DoesNotChangeStoredPassword()
    {
        var (_, vault) = await CreateAsync();
        await SeedScenarioAsync(vault);
        vault.SelectedServiceItem = ServiceItem(vault, "ゲームA");
        _dialogs.EditorHandler = editor =>
        {
            editor.Label = "改名";
            return true;
        };

        await vault.EditAccountAsync(vault.Accounts.Single());

        var account = vault.Snapshot.Document.Accounts.Single(a => a.ServiceId == ServiceItem(vault, "ゲームA").ServiceId);
        Assert.Equal("改名", account.Label);
        Assert.Equal("game-pw", account.Password);
    }

    [Fact]
    public async Task SaveFailure_ShowsErrorAndKeepsPreviousState()
    {
        var (_, vault) = await CreateAsync();
        _dialogs.TextAnswers.Enqueue("Google");
        await vault.AddServiceAsync();
        _fs.FailReplace = true;
        _dialogs.TextAnswers.Enqueue("Amazon");

        await vault.AddServiceAsync();

        Assert.Single(vault.ServiceItems, s => s.ServiceId is not null);
        Assert.False(vault.IsSaving);
        Assert.Contains("保存", vault.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ManualBackup_CopiesEncryptedVault_AndMentionsOldDataRemains()
    {
        var (_, vault) = await CreateAsync();
        await SeedScenarioAsync(vault);
        using var other = new TempDataDir();
        _dialogs.BackupDestinationAnswer = Path.Combine(other.Path, "backup.dat");

        await vault.CreateManualBackupAsync();

        Assert.True(File.Exists(_dialogs.BackupDestinationAnswer));
        Assert.Contains(_dialogs.Messages, m => m.Message.Contains("バックアップ", StringComparison.Ordinal) && m.Message.Contains("残", StringComparison.Ordinal));
        Assert.Contains("バックアップ", vault.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChangeMasterPassword_WarnsAboutOldBackups_AndNewPasswordWorks()
    {
        var (shell, vault) = await CreateAsync();
        _dialogs.MasterPasswordAnswer = new ChangeMasterPasswordRequest(Password, "新しいマスターパスワード 架空 2026");

        await vault.ChangeMasterPasswordAsync();

        Assert.Contains(_dialogs.Messages, m => m.Message.Contains("旧", StringComparison.Ordinal) && m.Message.Contains("バックアップ", StringComparison.Ordinal));
        await shell.LockAsync(LockReason.Manual);
        await shell.UnlockAsync("新しいマスターパスワード 架空 2026");
        Assert.Equal(ShellState.Unlocked, shell.State);
    }

    [Fact]
    public async Task ChangeMasterPassword_WrongCurrentPassword_IsRejected()
    {
        var (shell, vault) = await CreateAsync();
        _dialogs.MasterPasswordAnswer = new ChangeMasterPasswordRequest("wrong", "新しいマスターパスワード 架空 2026");

        await vault.ChangeMasterPasswordAsync();

        Assert.Contains(_dialogs.Messages, m => m.Message.Contains("現在のマスターパスワード", StringComparison.Ordinal));
        await shell.LockAsync(LockReason.Manual);
        await shell.UnlockAsync(Password);
        Assert.Equal(ShellState.Unlocked, shell.State);
    }

    [Fact]
    public async Task Restore_ReplacesWholeDocument_AfterConfirmation()
    {
        var (_, vault) = await CreateAsync();
        await SeedScenarioAsync(vault);
        using var other = new TempDataDir();
        var backupPath = Path.Combine(other.Path, "backup.dat");
        _dialogs.BackupDestinationAnswer = backupPath;
        await vault.CreateManualBackupAsync();
        _dialogs.TextAnswers.Enqueue("追加サービス");
        await vault.AddServiceAsync();
        Assert.Equal(4, vault.Snapshot.Document.Services.Count);
        _dialogs.RestoreAnswer = new RestoreRequest(backupPath, Password);

        await vault.RestoreAsync();

        Assert.Contains("置き換え", Assert.Single(_dialogs.Confirms).Message, StringComparison.Ordinal);
        Assert.Equal(3, vault.Snapshot.Document.Services.Count);
        Assert.Contains("復元", vault.StatusMessage, StringComparison.Ordinal);
        Assert.Single(Directory.GetFiles(Path.Combine(_dir.Path, "backups"), "pre-restore-*.dat"));
    }

    private sealed class FaultyFileSystemForApp : IVaultFileSystem
    {
        private readonly PhysicalFileSystem _inner = new();

        public bool FailReplace { get; set; }

        public bool FileExists(string path) => _inner.FileExists(path);

        public long GetFileLength(string path) => _inner.GetFileLength(path);

        public byte[] ReadAllBytes(string path) => _inner.ReadAllBytes(path);

        public void WriteNewFileDurably(string path, ReadOnlySpan<byte> content) => _inner.WriteNewFileDurably(path, content);

        public void ReplaceFile(string sourcePath, string destinationPath, string backupPath)
        {
            if (FailReplace)
            {
                throw new IOException("テスト用の置き換え失敗");
            }

            _inner.ReplaceFile(sourcePath, destinationPath, backupPath);
        }

        public void MoveFile(string sourcePath, string destinationPath) => _inner.MoveFile(sourcePath, destinationPath);

        public void CopyFile(string sourcePath, string destinationPath, bool overwrite) => _inner.CopyFile(sourcePath, destinationPath, overwrite);

        public void DeleteFile(string path) => _inner.DeleteFile(path);

        public void CreateDirectory(string path) => _inner.CreateDirectory(path);

        public IReadOnlyList<string> ListFiles(string directory, string searchPattern) => _inner.ListFiles(directory, searchPattern);
    }

    [Fact]
    public async Task Restore_AppliesTheRestoredPreferencesToTheLockController()
    {
        var (shell, vault) = await CreateAsync();
        await SeedScenarioAsync(vault);
        using var other = new TempDataDir();
        var backupPath = Path.Combine(other.Path, "backup.dat");
        _dialogs.BackupDestinationAnswer = backupPath;
        await vault.CreateManualBackupAsync();
        _dialogs.SettingsHandler = settings =>
        {
            settings.AutoLockMinutes = 10;
            return true;
        };
        await vault.OpenSettingsAsync();
        Assert.Equal(TimeSpan.FromMinutes(10), shell.LockController.IdleTimeout);
        _dialogs.RestoreAnswer = new RestoreRequest(backupPath, Password);

        await vault.RestoreAsync();

        Assert.Equal(5, vault.Snapshot.Document.Preferences.AutoLockMinutes);
        Assert.Equal(TimeSpan.FromMinutes(5), shell.LockController.IdleTimeout);
    }

    [Fact]
    public async Task Copy_OvertakenByANewerCopyWhileBusy_LeavesTheNewerCopyAndItsStatus()
    {
        VaultViewModel? vault = null;
        var overtaken = false;
        var (_, created) = await CreateAsync(async _ =>
        {
            if (!overtaken && vault is not null)
            {
                // 古いコピーが Busy で待っている間に、利用者が別のアカウントのパスワードをコピーした
                overtaken = true;
                await vault.CopyPasswordAsync(vault.Accounts.Single(a => a.LoginId == "sub@example.com"));
            }
        });
        vault = created;
        await SeedScenarioAsync(vault);
        _device.BusySetCount = 1;

        await vault.CopyPasswordAsync(vault.Accounts.Single(a => a.LoginId == "main@example.com" && !a.IsReference));

        Assert.True(overtaken);
        Assert.Equal("google-sub-pw", _device.CurrentText);
        Assert.Contains("コピーしました", vault.StatusMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("できませんでした", vault.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Restore_TellsWhichPreferencesTheBackupChanged()
    {
        var (_, vault) = await CreateAsync();
        await SeedScenarioAsync(vault);
        using var other = new TempDataDir();
        var backupPath = Path.Combine(other.Path, "backup.dat");
        _dialogs.BackupDestinationAnswer = backupPath;
        await vault.CreateManualBackupAsync();
        _dialogs.SettingsHandler = settings =>
        {
            settings.AutoLockMinutes = 2;
            settings.ClipboardClearSeconds = 10;
            return true;
        };
        await vault.OpenSettingsAsync();
        await vault.ToggleScreenCaptureExclusionAsync();
        _dialogs.Messages.Clear();
        _dialogs.RestoreAnswer = new RestoreRequest(backupPath, Password);

        await vault.RestoreAsync();

        // バックアップに入っていた（緩い）設定が黙って適用されないよう、完了メッセージで差分を示す
        var done = Assert.Single(_dialogs.Messages, m => m.Title == "復元しました");
        Assert.Contains("設定", done.Message, StringComparison.Ordinal);
        Assert.Contains("2 分 → 5 分", done.Message, StringComparison.Ordinal);
        Assert.Contains("10 秒 → 30 秒", done.Message, StringComparison.Ordinal);
        Assert.Contains("画面キャプチャから除外 オン → オフ", done.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("表示時間", done.Message, StringComparison.Ordinal);
        Assert.False(vault.IsScreenCaptureExcluded);
    }

    [Fact]
    public async Task ToggleScreenCaptureExclusion_IsSavedInTheVault_AndAppliedToTheShell()
    {
        var (shell, vault) = await CreateAsync();
        Assert.False(vault.IsScreenCaptureExcluded);
        Assert.False(shell.IsScreenCaptureExcluded);

        await vault.ToggleScreenCaptureExclusionAsync();

        Assert.True(vault.IsScreenCaptureExcluded);
        Assert.True(vault.Snapshot.Document.Preferences.ExcludeFromScreenCapture);
        Assert.True(shell.IsScreenCaptureExcluded);
        Assert.Contains("除外", vault.StatusMessage, StringComparison.Ordinal);

        await vault.ToggleScreenCaptureExclusionAsync();

        Assert.False(vault.IsScreenCaptureExcluded);
        Assert.False(vault.Snapshot.Document.Preferences.ExcludeFromScreenCapture);
        Assert.False(shell.IsScreenCaptureExcluded);
    }

    [Fact]
    public async Task Settings_SavingOtherValues_KeepsTheScreenCaptureChoice()
    {
        var (_, vault) = await CreateAsync();
        await vault.ToggleScreenCaptureExclusionAsync();
        _dialogs.SettingsHandler = settings =>
        {
            settings.AutoLockMinutes = 10;
            return true;
        };

        await vault.OpenSettingsAsync();

        Assert.Equal(10, vault.Snapshot.Document.Preferences.AutoLockMinutes);
        Assert.True(vault.Snapshot.Document.Preferences.ExcludeFromScreenCapture);
    }

    [Fact]
    public async Task ToggleScreenCaptureExclusion_WhenSavingFails_KeepsTheOldChoice_AndReportsIt()
    {
        var (shell, vault) = await CreateAsync();
        _fs.FailReplace = true;

        await vault.ToggleScreenCaptureExclusionAsync();

        Assert.False(vault.IsScreenCaptureExcluded);
        Assert.False(shell.IsScreenCaptureExcluded);
        Assert.Contains("保存に失敗", vault.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddAccount_WithGeneratedPassword_SavesIt_AndRemembersTheGeneratorOptionsInTheSameCommit()
    {
        var (_, vault) = await CreateAsync();
        await SeedScenarioAsync(vault);
        var generationsBefore = Directory.GetFiles(Path.Combine(_dir.Path, "backups"), "vault-*.dat").Length;
        string? generated = null;
        _dialogs.GeneratorHandler = generator =>
        {
            generator.LengthChoice = PasswordLengthChoice.Sixteen;
            generator.UseSymbols = false;
            generated = generator.Candidate;
            return true;
        };
        _dialogs.EditorHandlerAsync = async editor =>
        {
            editor.SelectedService = editor.Services.First(s => s.Name == "Amazon");
            editor.LoginIdText = "gen-user";
            // 画面の「生成...」ボタンに相当
            var value = await editor.GeneratePasswordAsync();
            Assert.Equal(generated, value);
            return true;
        };

        await vault.AddAccountAsync();

        var account = vault.Snapshot.Document.Accounts.Single(a => a.LoginIdText == "gen-user");
        Assert.NotNull(generated);
        Assert.Equal(16, generated!.Length);
        Assert.Equal(generated, account.Password);
        var options = vault.Snapshot.Document.Preferences.PasswordGenerator;
        Assert.Equal(16, options.Length);
        Assert.False(options.UseSymbols);
        Assert.Equal(generationsBefore + 1, Directory.GetFiles(Path.Combine(_dir.Path, "backups"), "vault-*.dat").Length);
    }

    [Fact]
    public async Task EditAccount_GeneratorStartsFromTheSavedOptions_AndCancelKeepsEverything()
    {
        var (_, vault) = await CreateAsync();
        await SeedScenarioAsync(vault);
        var seen = new List<int>();
        _dialogs.GeneratorHandler = generator =>
        {
            seen.Add(generator.ToOptions().Length);
            return false;
        };
        _dialogs.EditorHandlerAsync = async editor =>
        {
            Assert.Null(await editor.GeneratePasswordAsync());
            Assert.Equal(PasswordEditMode.Keep, editor.PasswordMode);
            return true;
        };
        var before = vault.Snapshot.Document;

        await vault.EditAccountAsync(vault.Accounts.Single(a => a.LoginId == "player01"));

        Assert.Equal(new[] { 12 }, seen);
        Assert.Equal(before.Preferences, vault.Snapshot.Document.Preferences);
        Assert.Equal("game-pw", vault.Snapshot.Document.Accounts.Single(a => a.LoginIdText == "player01").Password);
    }

    private int GenerationCount() => Directory.GetFiles(Path.Combine(_dir.Path, "backups"), "vault-*.dat").Length;

    [Fact]
    public async Task AddAccount_WithANewService_SavesServiceAndAccountInOneSave_AndSelectsThem()
    {
        var (_, vault) = await CreateAsync();
        await SeedScenarioAsync(vault);
        var generationsBefore = GenerationCount();
        _dialogs.EditorHandler = editor =>
        {
            editor.SelectedService = editor.ServiceOptions[^1];
            editor.NewServiceName = " ゲームB ";
            editor.LoginIdText = "player9";
            editor.SetNewPassword("gameb-pw");
            return true;
        };

        await vault.AddAccountAsync();

        var service = vault.Snapshot.Document.Services.Single(s => s.Name == "ゲームB");
        var account = vault.Snapshot.Document.Accounts.Single(a => a.LoginIdText == "player9");
        Assert.Equal(service.Id, account.ServiceId);
        Assert.Equal(generationsBefore + 1, GenerationCount());
        Assert.Equal("ゲームB", vault.SelectedServiceItem?.Name);
        Assert.Equal(account.Id, vault.SelectedAccount?.Id);
        Assert.Contains("サービス「ゲームB」", vault.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddAccount_WithANewServiceNameThatAlreadyExists_AddsToTheExistingService()
    {
        var (_, vault) = await CreateAsync();
        await SeedScenarioAsync(vault);
        var servicesBefore = vault.Snapshot.Document.Services.Count;
        _dialogs.EditorHandler = editor =>
        {
            editor.SelectedService = editor.ServiceOptions[^1];
            editor.NewServiceName = " amazon ";
            editor.LoginIdText = "second-amazon";
            editor.SetNewPassword("amazon-pw-2");
            return true;
        };

        await vault.AddAccountAsync();

        Assert.Equal(servicesBefore, vault.Snapshot.Document.Services.Count);
        var amazon = vault.Snapshot.Document.Services.Single(s => s.Name == "Amazon");
        Assert.Equal(amazon.Id, vault.Snapshot.Document.Accounts.Single(a => a.LoginIdText == "second-amazon").ServiceId);
        Assert.Contains("既存のサービス「Amazon」", vault.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddAccount_WhenNoServiceExists_OpensTheEditorWithANewService_AndCancelCreatesNothing()
    {
        var (_, vault) = await CreateAsync();
        AccountEditorViewModel? shown = null;
        _dialogs.EditorHandler = editor =>
        {
            shown = editor;
            editor.NewServiceName = "Google";
            return false;
        };

        await vault.AddAccountAsync();

        Assert.NotNull(shown);
        Assert.True(shown!.IsNewService);
        Assert.Empty(_dialogs.Messages);
        Assert.Empty(vault.Snapshot.Document.Services);
    }

    [Fact]
    public async Task EditAccount_CanMoveTheAccountToANewService()
    {
        var (_, vault) = await CreateAsync();
        await SeedScenarioAsync(vault);
        var generationsBefore = GenerationCount();
        _dialogs.EditorHandler = editor =>
        {
            editor.SelectedService = editor.ServiceOptions[^1];
            editor.NewServiceName = "ゲームC";
            return true;
        };

        await vault.EditAccountAsync(vault.Accounts.Single(a => a.LoginId == "player01"));

        var moved = vault.Snapshot.Document.Accounts.Single(a => a.LoginIdText == "player01");
        Assert.Equal(vault.Snapshot.Document.Services.Single(s => s.Name == "ゲームC").Id, moved.ServiceId);
        Assert.Equal("game-pw", moved.Password);
        Assert.Contains(vault.Snapshot.Document.Services, s => s.Name == "ゲームA");
        Assert.Equal(generationsBefore + 1, GenerationCount());
    }
}
