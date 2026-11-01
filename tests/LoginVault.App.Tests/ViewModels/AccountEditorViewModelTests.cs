using LoginVault.App.ViewModels;
using LoginVault.Core.Model;
using LoginVault.Core.Operations;

namespace LoginVault.App.Tests.ViewModels;

public class AccountEditorViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 12, 0, 0, TimeSpan.FromHours(9));

    private static (VaultDocument Doc, ServiceEntry Google, ServiceEntry Amazon, AccountEntry GoogleMain, AccountEntry GoogleSub, AccountEntry AmazonPersonal) Scenario()
    {
        var google = new ServiceEntry { Id = Guid.NewGuid(), Name = "Google", CreatedAt = Now, UpdatedAt = Now };
        var amazon = new ServiceEntry { Id = Guid.NewGuid(), Name = "Amazon", CreatedAt = Now, UpdatedAt = Now };
        var googleMain = new AccountEntry { Id = Guid.NewGuid(), ServiceId = google.Id, Label = "メイン", LoginIdMode = LoginIdMode.Direct, LoginIdText = "main@example.com", Password = "google-main-pw", IsEmailAccount = true, CreatedAt = Now, UpdatedAt = Now };
        var googleSub = new AccountEntry { Id = Guid.NewGuid(), ServiceId = google.Id, Label = "サブ", LoginIdMode = LoginIdMode.Direct, LoginIdText = "sub@example.com", Password = "google-sub-pw", IsEmailAccount = true, CreatedAt = Now, UpdatedAt = Now };
        var amazonPersonal = new AccountEntry { Id = Guid.NewGuid(), ServiceId = amazon.Id, Label = "個人用", LoginIdMode = LoginIdMode.EmailReference, LoginEmailAccountId = googleMain.Id, Password = "amazon-pw", CreatedAt = Now, UpdatedAt = Now };
        var doc = new VaultDocument { Services = new[] { google, amazon }, Accounts = new[] { googleMain, googleSub, amazonPersonal } };
        return (doc, google, amazon, googleMain, googleSub, amazonPersonal);
    }

    [Fact]
    public void NewAccount_DefaultsToDirectModeAndNewPassword()
    {
        var sc = Scenario();
        var editor = new AccountEditorViewModel(sc.Doc, null, sc.Amazon.Id);

        Assert.True(editor.IsNew);
        Assert.False(editor.UseEmailReference);
        Assert.Equal(PasswordEditMode.Set, editor.PasswordMode);
        Assert.False(editor.CanKeepPassword);
        Assert.Equal(sc.Amazon.Id, editor.SelectedService?.Id);
        Assert.Equal(MfaStatus.Unverified, editor.MfaStatus);
    }

    [Fact]
    public void EmailAccount_ForcesDirectMode_AndDisablesReferenceOption()
    {
        var sc = Scenario();
        var editor = new AccountEditorViewModel(sc.Doc, null, sc.Amazon.Id) { UseEmailReference = true };

        editor.IsEmailAccount = true;

        Assert.False(editor.UseEmailReference);
        Assert.False(editor.CanUseEmailReference);
    }

    [Fact]
    public void EmailCandidates_ExcludeSelf_AndNonEmailAccounts()
    {
        var sc = Scenario();
        var editingMain = new AccountEditorViewModel(sc.Doc, sc.GoogleMain, null);
        var editingAmazon = new AccountEditorViewModel(sc.Doc, sc.AmazonPersonal, null);

        Assert.Equal(new[] { sc.GoogleSub.Id }, editingMain.EmailCandidates.Select(c => c.AccountId).ToArray());
        Assert.Equal(2, editingAmazon.EmailCandidates.Count);
        Assert.Contains(editingAmazon.EmailCandidates, c => c.Label == "main@example.com — Google／メイン");
        Assert.Equal(sc.GoogleMain.Id, editingAmazon.SelectedEmailCandidate?.AccountId);
    }

    [Fact]
    public void EditingExisting_DefaultsToKeepPassword()
    {
        var sc = Scenario();
        var editor = new AccountEditorViewModel(sc.Doc, sc.GoogleSub, null);

        Assert.False(editor.IsNew);
        Assert.True(editor.CanKeepPassword);
        Assert.Equal(PasswordEditMode.Keep, editor.PasswordMode);
        Assert.True(editor.TryBuildInput(out var input, out var error), error);
        Assert.IsType<PasswordChange.KeepPassword>(input!.Password);
        Assert.Equal("google-sub-pw", input.Password.Resolve("google-sub-pw"));
    }

    [Fact]
    public void SetMode_UsesNewPasswordExactly()
    {
        var sc = Scenario();
        var editor = new AccountEditorViewModel(sc.Doc, sc.GoogleSub, null) { PasswordMode = PasswordEditMode.Set };
        editor.SetNewPassword("  new pw 🔑 ");

        Assert.True(editor.TryBuildInput(out var input, out _));
        Assert.Equal("  new pw 🔑 ", input!.Password.Resolve("old"));
    }

    [Fact]
    public void ClearMode_ProducesExplicitEmptyPassword()
    {
        var sc = Scenario();
        var editor = new AccountEditorViewModel(sc.Doc, sc.GoogleSub, null) { PasswordMode = PasswordEditMode.Clear };

        Assert.True(editor.TryBuildInput(out var input, out _));
        Assert.IsType<PasswordChange.ClearPassword>(input!.Password);
    }

    [Fact]
    public void NewAccount_WithEmptyNewPassword_ProducesClear_NotKeep()
    {
        var sc = Scenario();
        var editor = new AccountEditorViewModel(sc.Doc, null, sc.Amazon.Id) { LoginIdText = "user01" };

        Assert.True(editor.TryBuildInput(out var input, out _));
        Assert.IsType<PasswordChange.ClearPassword>(input!.Password);
    }

    [Fact]
    public void DirectMode_WithoutLoginId_IsError()
    {
        var sc = Scenario();
        var editor = new AccountEditorViewModel(sc.Doc, null, sc.Amazon.Id) { LoginIdText = "   " };

        Assert.False(editor.TryBuildInput(out _, out var error));
        Assert.Contains("ログインID", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ReferenceMode_WithoutSelection_IsError()
    {
        var sc = Scenario();
        var editor = new AccountEditorViewModel(sc.Doc, null, sc.Amazon.Id) { UseEmailReference = true, SelectedEmailCandidate = null };

        Assert.False(editor.TryBuildInput(out _, out var error));
        Assert.Contains("メール", error, StringComparison.Ordinal);
    }

    [Fact]
    public void MfaEnabled_WithoutMethods_IsError()
    {
        var sc = Scenario();
        var editor = new AccountEditorViewModel(sc.Doc, sc.GoogleSub, null) { MfaStatus = MfaStatus.Enabled };

        Assert.False(editor.TryBuildInput(out _, out var error));
        Assert.Contains("方式", error, StringComparison.Ordinal);
    }

    [Fact]
    public void OtherMethod_WithoutName_IsError()
    {
        var sc = Scenario();
        var editor = new AccountEditorViewModel(sc.Doc, sc.GoogleSub, null) { MfaStatus = MfaStatus.Enabled, UseOther = true, OtherName = " " };

        Assert.False(editor.TryBuildInput(out _, out var error));
        Assert.Contains("その他", error, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildInput_MapsAllFields()
    {
        var sc = Scenario();
        var editor = new AccountEditorViewModel(sc.Doc, null, sc.Google.Id)
        {
            Label = "  仕事用 ",
            IsEmailAccount = true,
            LoginIdText = "work@example.com",
            MfaStatus = MfaStatus.Enabled,
            UseSms = true,
            SmsDetail = "末尾5678",
            UseOther = true,
            OtherName = "YubiKey",
            OtherDetail = "鍵1本",
            Notes = "メモ",
        };
        editor.SetNewPassword("pw");

        Assert.True(editor.TryBuildInput(out var input, out var error), error);
        Assert.Equal(sc.Google.Id, input!.ServiceId);
        Assert.Equal("  仕事用 ", input.Label);
        Assert.True(input.IsEmailAccount);
        Assert.Equal(LoginIdMode.Direct, input.LoginIdMode);
        Assert.Equal("work@example.com", input.LoginIdText);
        Assert.Equal(MfaStatus.Enabled, input.MfaStatus);
        Assert.Equal(2, input.MfaMethods.Count);
        Assert.Contains(input.MfaMethods, m => m.Type == MfaMethodType.Sms && m.Detail == "末尾5678");
        Assert.Contains(input.MfaMethods, m => m.Type == MfaMethodType.Other && m.CustomType == "YubiKey" && m.Detail == "鍵1本");
        Assert.Equal("メモ", input.Notes);
    }

    [Fact]
    public void EditingExisting_LoadsMfaMethodsIntoFlags()
    {
        var sc = Scenario();
        var account = sc.GoogleMain with
        {
            MfaStatus = MfaStatus.Enabled,
            MfaMethods = new[] { new MfaMethod(MfaMethodType.Authenticator, Detail: "スマホ"), new MfaMethod(MfaMethodType.Other, "物理キー") },
        };

        var editor = new AccountEditorViewModel(sc.Doc, account, null);

        Assert.True(editor.UseAuthenticator);
        Assert.Equal("スマホ", editor.AuthenticatorDetail);
        Assert.True(editor.UseOther);
        Assert.Equal("物理キー", editor.OtherName);
        Assert.False(editor.UseSms);
    }

    [Fact]
    public void MfaNotEnabled_DropsMethodsFromInput()
    {
        var sc = Scenario();
        var editor = new AccountEditorViewModel(sc.Doc, sc.GoogleSub, null) { MfaStatus = MfaStatus.NotConfigured, UseSms = true };

        Assert.True(editor.TryBuildInput(out var input, out _));
        Assert.Empty(input!.MfaMethods);
    }

    [Fact]
    public async Task GeneratePassword_StoresThePendingPassword_AndSwitchesToNewPasswordMode()
    {
        var sc = Scenario();
        var editor = new AccountEditorViewModel(sc.Doc, sc.AmazonPersonal, null);
        Assert.Equal(PasswordEditMode.Keep, editor.PasswordMode);
        editor.GeneratePasswordHandler = () => Task.FromResult<string?>("generated-架空");

        var value = await editor.GeneratePasswordAsync();

        Assert.Equal("generated-架空", value);
        Assert.Equal("generated-架空", editor.PendingNewPassword);
        Assert.Equal(PasswordEditMode.Set, editor.PasswordMode);
        Assert.True(editor.TryBuildInput(out var input, out _));
        Assert.Equal("generated-架空", input!.Password.Resolve("old"));
    }

    [Fact]
    public async Task GeneratePassword_WhenCancelledOrUnavailable_LeavesTheEditorUnchanged()
    {
        var sc = Scenario();
        var editor = new AccountEditorViewModel(sc.Doc, sc.AmazonPersonal, null);
        editor.SetNewPassword("typed");

        Assert.Null(await editor.GeneratePasswordAsync());
        editor.GeneratePasswordHandler = () => Task.FromResult<string?>(null);
        Assert.Null(await editor.GeneratePasswordAsync());

        Assert.Equal("typed", editor.PendingNewPassword);
        Assert.Equal(PasswordEditMode.Keep, editor.PasswordMode);
    }

    [Fact]
    public void NewAccount_DetailsStartCollapsed_AndHoldNothing()
    {
        var sc = Scenario();
        var editor = new AccountEditorViewModel(sc.Doc, null, sc.Amazon.Id);

        Assert.False(editor.IsDetailsExpanded);
        Assert.False(editor.HasDetailSettings);
        Assert.Equal("詳細設定", editor.DetailsHeaderText);
    }

    [Fact]
    public void EditingAnAccountWithDetails_StartsCollapsed_ButSaysThatSettingsExist()
    {
        var sc = Scenario();
        var editor = new AccountEditorViewModel(sc.Doc, sc.GoogleMain, null);

        Assert.False(editor.IsDetailsExpanded);
        Assert.True(editor.HasDetailSettings);
        Assert.Equal("詳細設定（設定あり）", editor.DetailsHeaderText);

        editor.IsDetailsExpanded = true;
        Assert.Equal("詳細設定", editor.DetailsHeaderText);
    }

    [Fact]
    public void HasDetailSettings_FollowsEachDetailField()
    {
        var sc = Scenario();
        var editor = new AccountEditorViewModel(sc.Doc, null, sc.Amazon.Id);
        var changed = new List<string?>();
        editor.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        editor.Notes = "架空のメモ";
        Assert.True(editor.HasDetailSettings);
        Assert.Contains(nameof(AccountEditorViewModel.DetailsHeaderText), changed);
        editor.Notes = string.Empty;
        Assert.False(editor.HasDetailSettings);

        editor.Label = "メイン";
        Assert.True(editor.HasDetailSettings);
        editor.Label = string.Empty;

        editor.MfaStatus = MfaStatus.Enabled;
        Assert.True(editor.HasDetailSettings);
        editor.MfaStatus = MfaStatus.Unverified;
        Assert.False(editor.HasDetailSettings);
        editor.MfaStatus = MfaStatus.NotConfigured;
        Assert.False(editor.HasDetailSettings);

        // UseEmailReference は基本欄（ログインID項目の下）のトグルスイッチになったため詳細設定には影響しない（D-049）
        editor.UseEmailReference = true;
        Assert.False(editor.HasDetailSettings);
        editor.UseEmailReference = false;

        editor.IsEmailAccount = true;
        Assert.True(editor.HasDetailSettings);
    }

    [Fact]
    public void ErrorsAboutDetailFields_OpenTheDetails_ButErrorsAboutBasicFieldsDoNot()
    {
        var sc = Scenario();
        var editor = new AccountEditorViewModel(sc.Doc, null, sc.Amazon.Id);

        // ID が空: 基本欄の誤りなので閉じたまま
        Assert.False(editor.TryBuildInput(out _, out _));
        Assert.False(editor.IsDetailsExpanded);

        // 二段階認証「設定あり」で方式なし: 詳細設定の中の誤りなので開く
        editor.LoginIdText = "user01";
        editor.MfaStatus = MfaStatus.Enabled;
        Assert.False(editor.TryBuildInput(out _, out var error));
        Assert.Contains("二段階認証", error, StringComparison.Ordinal);
        Assert.True(editor.IsDetailsExpanded);

        // メールアカウント「はい」で ID がメール形式でない: フラグは詳細設定の中なので開く
        var other = new AccountEditorViewModel(sc.Doc, null, sc.Amazon.Id) { LoginIdText = "not-an-email", IsEmailAccount = true };
        Assert.False(other.TryBuildInput(out _, out _));
        Assert.True(other.IsDetailsExpanded);
    }

    [Fact]
    public void ShowError_FromCoreIssues_OpensTheDetailsOnlyForDetailFields()
    {
        var sc = Scenario();
        var detail = new AccountEditorViewModel(sc.Doc, null, sc.Amazon.Id);
        var basic = new AccountEditorViewModel(sc.Doc, null, sc.Amazon.Id);

        detail.ShowError("x", new[] { LoginVault.Core.Validation.ValidationIssue.Error(LoginVault.Core.Validation.IssueCode.AccountIsReferenced, "参照されています") });
        basic.ShowError("y", new[] { LoginVault.Core.Validation.ValidationIssue.Error(LoginVault.Core.Validation.IssueCode.LoginIdRequired, "ID は必須です") });

        Assert.Equal("x", detail.ErrorMessage);
        Assert.True(detail.IsDetailsExpanded);
        Assert.Equal("y", basic.ErrorMessage);
        Assert.False(basic.IsDetailsExpanded);
    }

    [Fact]
    public void ServiceOptions_EndWithANewServiceEntry_AndAVaultWithoutServicesStartsThere()
    {
        var sc = Scenario();
        var editor = new AccountEditorViewModel(sc.Doc, null, sc.Amazon.Id);

        Assert.Equal(sc.Doc.Services.Count + 1, editor.ServiceOptions.Count);
        Assert.Equal(AccountEditorViewModel.NewServiceChoice, editor.ServiceOptions[^1]);
        Assert.Equal("＋ 新しいサービスを追加…", editor.ServiceOptions[^1].Name);
        Assert.False(editor.IsNewService);

        var empty = new AccountEditorViewModel(new VaultDocument(), null, null);
        Assert.True(empty.IsNewService);
        Assert.Single(empty.ServiceOptions);
    }

    [Fact]
    public void NewService_RequiresAName_WithoutOpeningTheDetails()
    {
        var sc = Scenario();
        var editor = new AccountEditorViewModel(sc.Doc, null, null) { LoginIdText = "user01" };
        var changed = new List<string?>();
        editor.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        editor.SelectedService = editor.ServiceOptions[^1];

        Assert.True(editor.IsNewService);
        Assert.Contains(nameof(AccountEditorViewModel.IsNewService), changed);
        Assert.False(editor.TryBuildInput(out _, out var error));
        Assert.Contains("サービス名", error, StringComparison.Ordinal);
        Assert.False(editor.IsDetailsExpanded);

        editor.NewServiceName = "ゲームB";
        Assert.True(editor.TryBuildInput(out _, out _));
    }

    [Fact]
    public void Mfa_OptionProperties_ReflectAndChangeState()
    {
        var sc = Scenario();
        var editor = new AccountEditorViewModel(sc.Doc, null, sc.Amazon.Id);

        // 新規作成時の初期状態: 未設定
        Assert.True(editor.IsMfaNotConfigured);
        Assert.False(editor.IsMfaConfigured);
        Assert.False(editor.IsMfaEnabled);

        var changed = new List<string?>();
        editor.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        // 「設定あり」を選択
        editor.IsMfaConfigured = true;
        Assert.Equal(MfaStatus.Enabled, editor.MfaStatus);
        Assert.True(editor.IsMfaConfigured);
        Assert.False(editor.IsMfaNotConfigured);
        Assert.True(editor.IsMfaEnabled);
        Assert.Contains(nameof(AccountEditorViewModel.IsMfaConfigured), changed);
        Assert.Contains(nameof(AccountEditorViewModel.IsMfaNotConfigured), changed);
        Assert.Contains(nameof(AccountEditorViewModel.IsMfaEnabled), changed);

        changed.Clear();

        // 「未設定」を選択
        editor.IsMfaNotConfigured = true;
        Assert.Equal(MfaStatus.NotConfigured, editor.MfaStatus);
        Assert.False(editor.IsMfaConfigured);
        Assert.True(editor.IsMfaNotConfigured);
        Assert.False(editor.IsMfaEnabled);
        Assert.Contains(nameof(AccountEditorViewModel.IsMfaConfigured), changed);
        Assert.Contains(nameof(AccountEditorViewModel.IsMfaNotConfigured), changed);
        Assert.Contains(nameof(AccountEditorViewModel.IsMfaEnabled), changed);

        // 過去データ（Unverified）の既存アカウントを開いた場合も「未設定」として扱われる
        var unverifiedAccount = sc.GoogleSub with { MfaStatus = MfaStatus.Unverified };
        var legacyEditor = new AccountEditorViewModel(sc.Doc, unverifiedAccount, null);
        Assert.True(legacyEditor.IsMfaNotConfigured);
        Assert.False(legacyEditor.IsMfaConfigured);
        Assert.False(legacyEditor.IsMfaEnabled);
    }
}
