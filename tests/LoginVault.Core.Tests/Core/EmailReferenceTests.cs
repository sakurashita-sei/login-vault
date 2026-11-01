using LoginVault.Core.Model;
using LoginVault.Core.Operations;
using LoginVault.Core.Validation;

namespace LoginVault.Core.Tests.Core;

public class EmailReferenceTests
{
    [Fact]
    public void EmailCandidates_ReturnsOnlyEmailAccounts()
    {
        var sc = Fx.Scenario();
        var candidates = VaultQueries.EmailCandidates(sc.Doc).Select(a => a.Id).ToList();

        Assert.Equal(2, candidates.Count);
        Assert.Contains(sc.GoogleMain.Id, candidates);
        Assert.Contains(sc.GoogleSub.Id, candidates);
        Assert.DoesNotContain(sc.AmazonPersonal.Id, candidates);
        Assert.DoesNotContain(sc.GamePlayer.Id, candidates);
    }

    [Fact]
    public void EmailCandidates_ExcludesEmailFormattedIdsThatAreNotEmailAccounts()
    {
        var s = Fx.Service("Amazon");
        var amazon = Fx.Direct(s.Id, "me@example.com", isEmailAccount: false);
        var doc = Fx.Doc(new[] { s }, new[] { amazon });

        Assert.Empty(VaultQueries.EmailCandidates(doc));
    }

    [Fact]
    public void EmailCandidates_ExcludesEmailAccountsThatAreNotDirect()
    {
        var s = Fx.Service("Mail");
        var email = Fx.Direct(s.Id, "a@example.com", isEmailAccount: true);
        var broken = Fx.Reference(s.Id, email.Id, isEmailAccount: true);
        var doc = Fx.Doc(new[] { s }, new[] { email, broken });

        var candidate = Assert.Single(VaultQueries.EmailCandidates(doc));
        Assert.Equal(email.Id, candidate.Id);
    }

    [Fact]
    public void EmailCandidateLabel_CombinesAddressServiceAndLabel()
    {
        var sc = Fx.Scenario();

        Assert.Equal("main@example.com — Google／メイン", VaultQueries.EmailCandidateLabel(sc.Doc, sc.GoogleMain));
    }

    [Fact]
    public void EmailCandidateLabel_OmitsLabelWhenEmpty()
    {
        var s = Fx.Service("Mail");
        var email = Fx.Direct(s.Id, "a@example.com", isEmailAccount: true);
        var doc = Fx.Doc(new[] { s }, new[] { email });

        Assert.Equal("a@example.com — Mail", VaultQueries.EmailCandidateLabel(doc, email));
    }

    [Fact]
    public void ResolveLoginId_Direct_ReturnsOwnText()
    {
        var sc = Fx.Scenario();
        var resolution = VaultQueries.ResolveLoginId(sc.Doc, sc.GamePlayer);

        Assert.False(resolution.IsBroken);
        Assert.Equal("player01", resolution.Value);
    }

    [Fact]
    public void ResolveLoginId_Reference_ReturnsTargetAddress()
    {
        var sc = Fx.Scenario();
        var resolution = VaultQueries.ResolveLoginId(sc.Doc, sc.AmazonPersonal);

        Assert.False(resolution.IsBroken);
        Assert.Equal("main@example.com", resolution.Value);
    }

    [Fact]
    public void ResolveLoginId_MissingTarget_IsBroken()
    {
        var s = Fx.Service("A");
        var dangling = Fx.Reference(s.Id, Guid.NewGuid());
        var doc = Fx.Doc(new[] { s }, new[] { dangling });

        var resolution = VaultQueries.ResolveLoginId(doc, dangling);

        Assert.True(resolution.IsBroken);
        Assert.Null(resolution.Value);
    }

    [Fact]
    public void ReferrersOf_ListsAccountsThatReferenceTheEmail()
    {
        var sc = Fx.Scenario();

        var referrer = Assert.Single(VaultQueries.ReferrersOf(sc.Doc, sc.GoogleMain.Id));
        Assert.Equal(sc.AmazonPersonal.Id, referrer.Id);
        Assert.Empty(VaultQueries.ReferrersOf(sc.Doc, sc.GoogleSub.Id));
    }

    [Fact]
    public void ConvertReferenceToDirect_CopiesCurrentAddressAndClearsReference()
    {
        var sc = Fx.Scenario();
        var result = VaultOperations.ConvertReferenceToDirect(sc.Doc, sc.AmazonPersonal.Id, Fx.Later);

        Assert.True(result.Succeeded);
        var converted = result.Document.Accounts.Single(a => a.Id == sc.AmazonPersonal.Id);
        Assert.Equal(LoginIdMode.Direct, converted.LoginIdMode);
        Assert.Equal("main@example.com", converted.LoginIdText);
        Assert.Null(converted.LoginEmailAccountId);
        Assert.Equal("amazon-pw", converted.Password);
        Assert.Empty(VaultQueries.ReferrersOf(result.Document, sc.GoogleMain.Id));
    }

    [Fact]
    public void ConvertReferenceToDirect_OnDirectAccount_Fails()
    {
        var sc = Fx.Scenario();
        var result = VaultOperations.ConvertReferenceToDirect(sc.Doc, sc.GamePlayer.Id, Fx.Later);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, i => i.Code == IssueCode.NotAReference);
    }

    [Fact]
    public void RetargetReference_ToAnotherEmailAccount_Succeeds()
    {
        var sc = Fx.Scenario();
        var result = VaultOperations.RetargetReference(sc.Doc, sc.AmazonPersonal.Id, sc.GoogleSub.Id, Fx.Later);

        Assert.True(result.Succeeded);
        Assert.Equal("sub@example.com", VaultQueries.ResolveLoginId(result.Document, result.Document.Accounts.Single(a => a.Id == sc.AmazonPersonal.Id)).Value);
    }

    [Fact]
    public void RetargetReference_ToNonEmailAccount_Fails()
    {
        var sc = Fx.Scenario();
        var result = VaultOperations.RetargetReference(sc.Doc, sc.AmazonPersonal.Id, sc.GamePlayer.Id, Fx.Later);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, i => i.Code == IssueCode.ReferenceTargetNotEmailAccount);
    }

    [Fact]
    public void RetargetReference_ToSelf_Fails()
    {
        var sc = Fx.Scenario();
        var result = VaultOperations.RetargetReference(sc.Doc, sc.AmazonPersonal.Id, sc.AmazonPersonal.Id, Fx.Later);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, i => i.Code == IssueCode.SelfReference);
    }

    [Fact]
    public void ChangingEmailAddress_PropagatesToReferrers()
    {
        var sc = Fx.Scenario();
        var input = AccountInput.FromAccount(sc.GoogleMain) with { LoginIdText = "renamed@example.com" };

        var result = VaultOperations.UpdateAccount(sc.Doc, sc.GoogleMain.Id, input, Fx.Later);

        Assert.True(result.Succeeded);
        var amazon = result.Document.Accounts.Single(a => a.Id == sc.AmazonPersonal.Id);
        Assert.Equal("renamed@example.com", VaultQueries.ResolveLoginId(result.Document, amazon).Value);
    }

    [Fact]
    public void ConvertingReferrerBeforeAddressChange_KeepsOldValueForReferrer()
    {
        var sc = Fx.Scenario();
        var changeInput = AccountInput.FromAccount(sc.GoogleMain) with { LoginIdText = "renamed@example.com" };

        var result = VaultOperations.ApplyAll(
            sc.Doc,
            d => VaultOperations.ConvertReferenceToDirect(d, sc.AmazonPersonal.Id, Fx.Later),
            d => VaultOperations.UpdateAccount(d, sc.GoogleMain.Id, changeInput, Fx.Later));

        Assert.True(result.Succeeded);
        var amazon = result.Document.Accounts.Single(a => a.Id == sc.AmazonPersonal.Id);
        var google = result.Document.Accounts.Single(a => a.Id == sc.GoogleMain.Id);
        Assert.Equal("main@example.com", VaultQueries.ResolveLoginId(result.Document, amazon).Value);
        Assert.Equal("renamed@example.com", google.LoginIdText);
    }

    [Fact]
    public void PasswordsAreIndependentBetweenEmailAndReferrer()
    {
        var sc = Fx.Scenario();
        var emailInput = AccountInput.FromAccount(sc.GoogleMain) with { Password = PasswordChange.Set("new-google-pw") };
        var afterEmailChange = VaultOperations.UpdateAccount(sc.Doc, sc.GoogleMain.Id, emailInput, Fx.Later).Document;
        var referrerInput = AccountInput.FromAccount(sc.AmazonPersonal) with { Password = PasswordChange.Set("new-amazon-pw") };
        var afterBoth = VaultOperations.UpdateAccount(afterEmailChange, sc.AmazonPersonal.Id, referrerInput, Fx.Later).Document;

        Assert.Equal("amazon-pw", afterEmailChange.Accounts.Single(a => a.Id == sc.AmazonPersonal.Id).Password);
        Assert.Equal("new-google-pw", afterBoth.Accounts.Single(a => a.Id == sc.GoogleMain.Id).Password);
        Assert.Equal("new-amazon-pw", afterBoth.Accounts.Single(a => a.Id == sc.AmazonPersonal.Id).Password);
    }

    [Fact]
    public void DeleteEmail_FailsWhileReferenced_ThenSucceedsAfterConversion()
    {
        var sc = Fx.Scenario();
        var direct = VaultOperations.DeleteAccount(sc.Doc, sc.GoogleMain.Id);
        var batched = VaultOperations.ApplyAll(
            sc.Doc,
            d => VaultOperations.ConvertReferenceToDirect(d, sc.AmazonPersonal.Id, Fx.Later),
            d => VaultOperations.DeleteAccount(d, sc.GoogleMain.Id));

        Assert.False(direct.Succeeded);
        Assert.True(batched.Succeeded);
        Assert.DoesNotContain(batched.Document.Accounts, a => a.Id == sc.GoogleMain.Id);
        Assert.Equal("main@example.com", batched.Document.Accounts.Single(a => a.Id == sc.AmazonPersonal.Id).LoginIdText);
    }

    [Fact]
    public void AddAccount_ReferencingNonEmailAccount_Fails()
    {
        var sc = Fx.Scenario();
        var result = VaultOperations.AddAccount(sc.Doc, Fx.ReferenceInput(sc.Amazon.Id, sc.GamePlayer.Id), Fx.Later);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, i => i.Code == IssueCode.ReferenceTargetNotEmailAccount);
    }

    [Fact]
    public void AddAccount_ReferencingEmailAccount_StoresReferenceNotCopy()
    {
        var sc = Fx.Scenario();
        var result = VaultOperations.AddAccount(sc.Doc, Fx.ReferenceInput(sc.Game.Id, sc.GoogleSub.Id, label: "連携"), Fx.Later);

        Assert.True(result.Succeeded);
        var added = result.Document.Accounts.Single(a => a.Id == result.CreatedId);
        Assert.Equal(LoginIdMode.EmailReference, added.LoginIdMode);
        Assert.Equal(sc.GoogleSub.Id, added.LoginEmailAccountId);
        Assert.Null(added.LoginIdText);
    }

    [Fact]
    public void DisplayName_UsesLabelOrFallsBackToResolvedId()
    {
        var sc = Fx.Scenario();
        var s = Fx.Service("A");
        var unlabeled = Fx.Direct(s.Id, "player99");
        var dangling = Fx.Reference(s.Id, Guid.NewGuid());
        var doc = Fx.Doc(new[] { s }, new[] { unlabeled, dangling });

        Assert.Equal("個人用", VaultQueries.DisplayName(sc.Doc, sc.AmazonPersonal));
        Assert.Equal("player99", VaultQueries.DisplayName(doc, unlabeled));
        Assert.Equal("（参照エラー）", VaultQueries.DisplayName(doc, dangling));
    }
}
