using LoginVault.Core.Model;
using LoginVault.Core.Operations;
using LoginVault.Core.Validation;

namespace LoginVault.Core.Tests.Core;

public class VaultOperationsTests
{
    [Fact]
    public void AddService_CreatesServiceWithTrimmedName()
    {
        var result = VaultOperations.AddService(VaultDocument.Empty, "  Google  ", Fx.Now);

        Assert.True(result.Succeeded);
        var service = Assert.Single(result.Document.Services);
        Assert.Equal("Google", service.Name);
        Assert.Equal(service.Id, result.CreatedId);
        Assert.Equal(Fx.Now, service.CreatedAt);
    }

    [Fact]
    public void AddService_BlankName_Fails()
    {
        var result = VaultOperations.AddService(VaultDocument.Empty, "   ", Fx.Now);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, i => i.Code == IssueCode.ServiceNameRequired);
        Assert.Empty(result.Document.Services);
    }

    [Fact]
    public void AddService_DuplicateName_SucceedsWithWarning()
    {
        var doc = VaultOperations.AddService(VaultDocument.Empty, "Google", Fx.Now).Document;
        var result = VaultOperations.AddService(doc, "google", Fx.Now);

        Assert.True(result.Succeeded);
        Assert.Contains(result.Warnings, i => i.Code == IssueCode.DuplicateServiceName);
        Assert.Equal(2, result.Document.Services.Count);
    }

    [Fact]
    public void RenameService_UpdatesNameAndUpdatedAtOnly()
    {
        var sc = Fx.Scenario();
        var result = VaultOperations.RenameService(sc.Doc, sc.Game.Id, "ゲームB", Fx.Later);

        Assert.True(result.Succeeded);
        var renamed = result.Document.Services.Single(s => s.Id == sc.Game.Id);
        Assert.Equal("ゲームB", renamed.Name);
        Assert.Equal(Fx.Now, renamed.CreatedAt);
        Assert.Equal(Fx.Later, renamed.UpdatedAt);
    }

    [Fact]
    public void RenameService_UnknownId_Fails()
    {
        var result = VaultOperations.RenameService(Fx.Scenario().Doc, Guid.NewGuid(), "X", Fx.Later);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, i => i.Code == IssueCode.ServiceNotFound);
    }

    [Fact]
    public void DeleteService_WithoutExternalReferences_RemovesServiceAndItsAccounts()
    {
        var sc = Fx.Scenario();
        var result = VaultOperations.DeleteService(sc.Doc, sc.Game.Id);

        Assert.True(result.Succeeded);
        Assert.DoesNotContain(result.Document.Services, s => s.Id == sc.Game.Id);
        Assert.DoesNotContain(result.Document.Accounts, a => a.ServiceId == sc.Game.Id);
        Assert.Equal(3, result.Document.Accounts.Count);
    }

    [Fact]
    public void DeleteService_WithExternalReference_Fails()
    {
        var sc = Fx.Scenario();
        var result = VaultOperations.DeleteService(sc.Doc, sc.Google.Id);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, i => i.Code == IssueCode.ServiceHasExternalReferences);
        Assert.Equal(3, result.Document.Services.Count);
    }

    [Fact]
    public void DeleteService_WithOnlyInternalReferences_Succeeds()
    {
        var s = Fx.Service("Mail");
        var email = Fx.Direct(s.Id, "a@example.com", isEmailAccount: true);
        var internalRef = Fx.Reference(s.Id, email.Id);
        var doc = Fx.Doc(new[] { s }, new[] { email, internalRef });

        var result = VaultOperations.DeleteService(doc, s.Id);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Document.Accounts);
    }

    [Fact]
    public void AddAccount_Direct_StoresPasswordExactly()
    {
        var sc = Fx.Scenario();
        const string password = "  pässwörd 日本語 🔑  ";
        var input = Fx.Input(sc.Game.Id, "player02", PasswordChange.Set(password), label: "サブ");

        var result = VaultOperations.AddAccount(sc.Doc, input, Fx.Later);

        Assert.True(result.Succeeded);
        var added = result.Document.Accounts.Single(a => a.Id == result.CreatedId);
        Assert.Equal(password, added.Password);
        Assert.Equal("player02", added.LoginIdText);
        Assert.Equal(LoginIdMode.Direct, added.LoginIdMode);
        Assert.Equal(Fx.Later, added.CreatedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void AddAccount_MissingLoginId_Fails(string? loginId)
    {
        var sc = Fx.Scenario();
        var result = VaultOperations.AddAccount(sc.Doc, Fx.Input(sc.Game.Id, loginId), Fx.Later);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, i => i.Code == IssueCode.LoginIdRequired);
        Assert.Equal(4, result.Document.Accounts.Count);
    }

    [Fact]
    public void AddAccount_UnknownService_Fails()
    {
        var sc = Fx.Scenario();
        var result = VaultOperations.AddAccount(sc.Doc, Fx.Input(Guid.NewGuid(), "x"), Fx.Later);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, i => i.Code == IssueCode.UnknownService);
    }

    [Fact]
    public void AddAccount_EmptyPassword_SucceedsWithWarning()
    {
        var sc = Fx.Scenario();
        var result = VaultOperations.AddAccount(sc.Doc, Fx.Input(sc.Game.Id, "player02", PasswordChange.Clear), Fx.Later);

        Assert.True(result.Succeeded);
        Assert.Contains(result.Warnings, i => i.Code == IssueCode.EmptyPassword);
        Assert.Equal(string.Empty, result.Document.Accounts.Single(a => a.Id == result.CreatedId).Password);
    }

    [Fact]
    public void AddAccount_DuplicateLoginIdInSameService_Warns_ButOtherServiceDoesNot()
    {
        var sc = Fx.Scenario();
        var sameService = VaultOperations.AddAccount(sc.Doc, Fx.Input(sc.Game.Id, "PLAYER01"), Fx.Later);
        var otherService = VaultOperations.AddAccount(sc.Doc, Fx.Input(sc.Amazon.Id, "player01"), Fx.Later);

        Assert.True(sameService.Succeeded);
        Assert.Contains(sameService.Warnings, i => i.Code == IssueCode.DuplicateLoginIdInService);
        Assert.True(otherService.Succeeded);
        Assert.DoesNotContain(otherService.Warnings, i => i.Code == IssueCode.DuplicateLoginIdInService);
    }

    [Fact]
    public void UpdateAccount_KeepPassword_RetainsExistingValue()
    {
        var sc = Fx.Scenario();
        var input = AccountInput.FromAccount(sc.GamePlayer) with { Label = "変更後", Password = PasswordChange.Keep };

        var result = VaultOperations.UpdateAccount(sc.Doc, sc.GamePlayer.Id, input, Fx.Later);

        Assert.True(result.Succeeded);
        var updated = result.Document.Accounts.Single(a => a.Id == sc.GamePlayer.Id);
        Assert.Equal("game-pw", updated.Password);
        Assert.Equal("変更後", updated.Label);
    }

    [Fact]
    public void UpdateAccount_SetPassword_ReplacesValuePreservingWhitespace()
    {
        var sc = Fx.Scenario();
        var input = AccountInput.FromAccount(sc.GamePlayer) with { Password = PasswordChange.Set(" new pw ") };

        var result = VaultOperations.UpdateAccount(sc.Doc, sc.GamePlayer.Id, input, Fx.Later);

        Assert.True(result.Succeeded);
        Assert.Equal(" new pw ", result.Document.Accounts.Single(a => a.Id == sc.GamePlayer.Id).Password);
    }

    [Fact]
    public void UpdateAccount_ClearPassword_SetsEmptyWithWarning()
    {
        var sc = Fx.Scenario();
        var input = AccountInput.FromAccount(sc.GamePlayer) with { Password = PasswordChange.Clear };

        var result = VaultOperations.UpdateAccount(sc.Doc, sc.GamePlayer.Id, input, Fx.Later);

        Assert.True(result.Succeeded);
        Assert.Equal(string.Empty, result.Document.Accounts.Single(a => a.Id == sc.GamePlayer.Id).Password);
        Assert.Contains(result.Warnings, i => i.Code == IssueCode.EmptyPassword);
    }

    [Fact]
    public void UpdateAccount_UnknownId_Fails()
    {
        var sc = Fx.Scenario();
        var result = VaultOperations.UpdateAccount(sc.Doc, Guid.NewGuid(), AccountInput.FromAccount(sc.GamePlayer), Fx.Later);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, i => i.Code == IssueCode.AccountNotFound);
    }

    [Fact]
    public void UpdateAccount_PreservesCreatedAt_AndSetsUpdatedAt()
    {
        var sc = Fx.Scenario();
        var result = VaultOperations.UpdateAccount(sc.Doc, sc.GamePlayer.Id, AccountInput.FromAccount(sc.GamePlayer), Fx.Later);

        var updated = result.Document.Accounts.Single(a => a.Id == sc.GamePlayer.Id);
        Assert.Equal(Fx.Now, updated.CreatedAt);
        Assert.Equal(Fx.Later, updated.UpdatedAt);
    }

    [Fact]
    public void UpdateAccount_DoesNotChangeOtherAccounts()
    {
        var sc = Fx.Scenario();
        var input = AccountInput.FromAccount(sc.GamePlayer) with { Password = PasswordChange.Set("changed") };
        var result = VaultOperations.UpdateAccount(sc.Doc, sc.GamePlayer.Id, input, Fx.Later);

        Assert.Equal("amazon-pw", result.Document.Accounts.Single(a => a.Id == sc.AmazonPersonal.Id).Password);
        Assert.Equal("google-main-pw", result.Document.Accounts.Single(a => a.Id == sc.GoogleMain.Id).Password);
    }

    [Fact]
    public void DeleteAccount_Unreferenced_Succeeds()
    {
        var sc = Fx.Scenario();
        var result = VaultOperations.DeleteAccount(sc.Doc, sc.GoogleSub.Id);

        Assert.True(result.Succeeded);
        Assert.DoesNotContain(result.Document.Accounts, a => a.Id == sc.GoogleSub.Id);
    }

    [Fact]
    public void DeleteAccount_Referenced_Fails()
    {
        var sc = Fx.Scenario();
        var result = VaultOperations.DeleteAccount(sc.Doc, sc.GoogleMain.Id);

        Assert.False(result.Succeeded);
        var issue = Assert.Single(result.Errors, i => i.Code == IssueCode.AccountIsReferenced);
        Assert.Equal(sc.GoogleMain.Id, issue.AccountId);
        Assert.Contains(result.Document.Accounts, a => a.Id == sc.GoogleMain.Id);
    }

    [Fact]
    public void UpdateAccount_TurnOffEmailFlag_WhileReferenced_Fails()
    {
        var sc = Fx.Scenario();
        var input = AccountInput.FromAccount(sc.GoogleMain) with { IsEmailAccount = false };

        var result = VaultOperations.UpdateAccount(sc.Doc, sc.GoogleMain.Id, input, Fx.Later);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, i => i.Code == IssueCode.AccountIsReferenced);
    }

    [Fact]
    public void UpdateAccount_EmailAccountSwitchedToReferenceMode_Fails()
    {
        var sc = Fx.Scenario();
        var input = AccountInput.FromAccount(sc.GoogleSub) with
        {
            LoginIdMode = LoginIdMode.EmailReference,
            LoginIdText = null,
            LoginEmailAccountId = sc.GoogleMain.Id,
        };

        var result = VaultOperations.UpdateAccount(sc.Doc, sc.GoogleSub.Id, input, Fx.Later);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, i => i.Code == IssueCode.EmailAccountMustBeDirect);
    }

    [Fact]
    public void UpdateAccount_MfaEnabledWithoutMethods_Fails()
    {
        var sc = Fx.Scenario();
        var input = AccountInput.FromAccount(sc.GoogleSub) with { MfaStatus = MfaStatus.Enabled, MfaMethods = Array.Empty<MfaMethod>() };

        var result = VaultOperations.UpdateAccount(sc.Doc, sc.GoogleSub.Id, input, Fx.Later);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, i => i.Code == IssueCode.MfaMethodsRequiredWhenEnabled);
    }

    [Fact]
    public void UpdateAccount_OtherMethodWithoutName_Fails()
    {
        var sc = Fx.Scenario();
        var input = AccountInput.FromAccount(sc.GamePlayer) with { MfaMethods = new[] { new MfaMethod(MfaMethodType.Other, "") } };

        var result = VaultOperations.UpdateAccount(sc.Doc, sc.GamePlayer.Id, input, Fx.Later);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, i => i.Code == IssueCode.OtherMfaMethodNeedsCustomType);
    }

    [Fact]
    public void UpdatePreferences_Valid_Succeeds_AndInvalid_Fails()
    {
        var ok = VaultOperations.UpdatePreferences(VaultDocument.Empty, new VaultPreferences { AutoLockMinutes = 10 });
        var bad = VaultOperations.UpdatePreferences(VaultDocument.Empty, new VaultPreferences { RevealSeconds = 0 });

        Assert.True(ok.Succeeded);
        Assert.Equal(10, ok.Document.Preferences.AutoLockMinutes);
        Assert.False(bad.Succeeded);
        Assert.Contains(bad.Errors, i => i.Code == IssueCode.InvalidPreferences);
    }

    [Fact]
    public void ApplyAll_StopsAtFirstFailure_AndReturnsOriginalDocument()
    {
        var sc = Fx.Scenario();
        var result = VaultOperations.ApplyAll(
            sc.Doc,
            d => VaultOperations.AddService(d, "新サービス", Fx.Later),
            d => VaultOperations.DeleteAccount(d, sc.GoogleMain.Id));

        Assert.False(result.Succeeded);
        Assert.Same(sc.Doc, result.Document);
        Assert.Contains(result.Errors, i => i.Code == IssueCode.AccountIsReferenced);
    }

    [Fact]
    public void ApplyAll_AllSucceed_ReturnsFinalDocument()
    {
        var sc = Fx.Scenario();
        var result = VaultOperations.ApplyAll(
            sc.Doc,
            d => VaultOperations.AddService(d, "新サービス", Fx.Later),
            d => VaultOperations.DeleteAccount(d, sc.GoogleSub.Id));

        Assert.True(result.Succeeded);
        Assert.Equal(4, result.Document.Services.Count);
        Assert.DoesNotContain(result.Document.Accounts, a => a.Id == sc.GoogleSub.Id);
    }

    [Fact]
    public void ApplyAll_ReportsTheCreatedIdOfTheCreatingStep()
    {
        var sc = Fx.Scenario();

        var result = VaultOperations.ApplyAll(
            sc.Doc,
            d => VaultOperations.UpdatePreferences(d, d.Preferences with { AutoLockMinutes = 7 }),
            d => VaultOperations.AddAccount(d, Fx.Input(sc.Google.Id, "new-user"), Fx.Later));

        Assert.True(result.Succeeded);
        Assert.NotNull(result.CreatedId);
        Assert.Contains(result.Document.Accounts, a => a.Id == result.CreatedId && a.LoginIdText == "new-user");
        Assert.Equal(7, result.Document.Preferences.AutoLockMinutes);
    }

    [Fact]
    public void FindServiceByName_IgnoresCaseAndSurroundingSpaces()
    {
        var sc = Fx.Scenario();

        Assert.Equal(sc.Amazon.Id, VaultQueries.FindServiceByName(sc.Doc, "  aMaZoN ")?.Id);
        Assert.Null(VaultQueries.FindServiceByName(sc.Doc, "ゲームB"));
        Assert.Null(VaultQueries.FindServiceByName(sc.Doc, "   "));
    }

    [Fact]
    public void WithService_AddsANewService_AndTheAccountInIt_AsOneResult()
    {
        var sc = Fx.Scenario();

        var result = VaultOperations.WithService(sc.Doc, "  ゲームB ", Fx.Later, (d, serviceId) => VaultOperations.AddAccount(d, Fx.Input(serviceId, "player9"), Fx.Later));

        Assert.True(result.Succeeded);
        var service = Assert.Single(result.Document.Services, s => s.Name == "ゲームB");
        var account = Assert.Single(result.Document.Accounts, a => a.Id == result.CreatedId);
        Assert.Equal(service.Id, account.ServiceId);
        Assert.Equal(sc.Doc.Services.Count + 1, result.Document.Services.Count);
    }

    [Fact]
    public void WithService_ReusesAnExistingService_IgnoringCaseAndSurroundingSpaces()
    {
        var sc = Fx.Scenario();

        var result = VaultOperations.WithService(sc.Doc, " amazon ", Fx.Later, (d, serviceId) => VaultOperations.AddAccount(d, Fx.Input(serviceId, "second-user"), Fx.Later));

        Assert.True(result.Succeeded);
        Assert.Equal(sc.Doc.Services.Count, result.Document.Services.Count);
        Assert.Equal(sc.Amazon.Id, result.Document.Accounts.Single(a => a.Id == result.CreatedId).ServiceId);
    }

    [Fact]
    public void WithService_WhenTheNextStepFails_AddsNothing()
    {
        var sc = Fx.Scenario();

        var result = VaultOperations.WithService(sc.Doc, "ゲームB", Fx.Later, (d, serviceId) => VaultOperations.AddAccount(d, Fx.Input(serviceId, loginId: null), Fx.Later));

        Assert.False(result.Succeeded);
        Assert.Same(sc.Doc, result.Document);
        Assert.Contains(result.Errors, i => i.Code == IssueCode.LoginIdRequired);
    }

    [Fact]
    public void WithService_BlankName_IsRejected()
    {
        var sc = Fx.Scenario();

        var result = VaultOperations.WithService(sc.Doc, "   ", Fx.Later, (d, serviceId) => VaultOperations.AddAccount(d, Fx.Input(serviceId, "u"), Fx.Later));

        Assert.False(result.Succeeded);
        Assert.Same(sc.Doc, result.Document);
        Assert.Contains(result.Errors, i => i.Code == IssueCode.ServiceNameRequired);
    }
}
