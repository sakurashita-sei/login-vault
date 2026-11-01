using LoginVault.Core.Search;

namespace LoginVault.Core.Tests.Core;

public class SearchEngineTests
{
    private static IReadOnlyList<Guid> AccountIds(SearchResult result) => result.Accounts.Select(a => a.Id).ToList();

    [Fact]
    public void EmptyQuery_ReturnsEverything()
    {
        var sc = Fx.Scenario();
        var result = SearchEngine.Search(sc.Doc, "", AccountFilter.All);

        Assert.Equal(4, result.Accounts.Count);
        Assert.Equal(3, result.Services.Count);
    }

    [Fact]
    public void EmptyQuery_IncludesServicesWithoutAccounts()
    {
        var sc = Fx.Scenario();
        var doc = sc.Doc with { Services = sc.Doc.Services.Append(Fx.Service("空サービス")).ToArray() };

        var result = SearchEngine.Search(doc, null, AccountFilter.All);

        Assert.Equal(4, result.Services.Count);
    }

    [Fact]
    public void EmailOnlyFilter_ReturnsOnlyEmailAccounts()
    {
        var sc = Fx.Scenario();
        var result = SearchEngine.Search(sc.Doc, "", AccountFilter.EmailOnly);

        Assert.Equal(2, result.Accounts.Count);
        Assert.All(result.Accounts, a => Assert.True(a.IsEmailAccount));
        var service = Assert.Single(result.Services);
        Assert.Equal(sc.Google.Id, service.Id);
    }

    [Fact]
    public void ServiceNameMatch_ReturnsAllAccountsOfThatService()
    {
        var sc = Fx.Scenario();
        var result = SearchEngine.Search(sc.Doc, "google", AccountFilter.All);

        Assert.Equal(2, result.Accounts.Count);
        Assert.Contains(sc.GoogleMain.Id, AccountIds(result));
        Assert.Contains(sc.GoogleSub.Id, AccountIds(result));
    }

    [Fact]
    public void LabelMatch_ReturnsOnlyMatchingAccount()
    {
        var sc = Fx.Scenario();
        var result = SearchEngine.Search(sc.Doc, "サブ", AccountFilter.All);

        var account = Assert.Single(result.Accounts);
        Assert.Equal(sc.GoogleSub.Id, account.Id);
        var service = Assert.Single(result.Services);
        Assert.Equal(sc.Google.Id, service.Id);
    }

    [Fact]
    public void ResolvedIdMatch_FindsReferenceAccountToo()
    {
        var sc = Fx.Scenario();
        var result = SearchEngine.Search(sc.Doc, "main@example", AccountFilter.All);

        Assert.Equal(2, result.Accounts.Count);
        Assert.Contains(sc.GoogleMain.Id, AccountIds(result));
        Assert.Contains(sc.AmazonPersonal.Id, AccountIds(result));
    }

    [Fact]
    public void NotesMatch_FindsAccount()
    {
        var sc = Fx.Scenario();
        var result = SearchEngine.Search(sc.Doc, "プライム", AccountFilter.All);

        var account = Assert.Single(result.Accounts);
        Assert.Equal(sc.AmazonPersonal.Id, account.Id);
    }

    [Theory]
    [InlineData("authenticator")]
    [InlineData("スマホ")]
    public void MfaMethodTypeOrDetailMatch_FindsAccount(string query)
    {
        var sc = Fx.Scenario();
        var result = SearchEngine.Search(sc.Doc, query, AccountFilter.All);

        var account = Assert.Single(result.Accounts);
        Assert.Equal(sc.GoogleMain.Id, account.Id);
    }

    [Theory]
    [InlineData("yubikey")]
    [InlineData("その他")]
    [InlineData("鍵2本")]
    public void OtherMfaMethodNameOrDetailMatch_FindsAccount(string query)
    {
        var sc = Fx.Scenario();
        var result = SearchEngine.Search(sc.Doc, query, AccountFilter.All);

        var account = Assert.Single(result.Accounts);
        Assert.Equal(sc.GamePlayer.Id, account.Id);
    }

    [Theory]
    [InlineData("google-main-pw")]
    [InlineData("amazon-pw")]
    [InlineData("game-pw")]
    public void PasswordIsNeverSearchable(string password)
    {
        var sc = Fx.Scenario();
        var result = SearchEngine.Search(sc.Doc, password, AccountFilter.All);

        Assert.Empty(result.Accounts);
        Assert.Empty(result.Services);
    }

    [Fact]
    public void Search_IsCaseInsensitive()
    {
        var sc = Fx.Scenario();
        var result = SearchEngine.Search(sc.Doc, "AMAZON", AccountFilter.All);

        var account = Assert.Single(result.Accounts);
        Assert.Equal(sc.AmazonPersonal.Id, account.Id);
    }

    [Theory]
    [InlineData("google サブ")]
    [InlineData("google　サブ")]
    [InlineData("  サブ   GOOGLE ")]
    public void MultipleWords_AreCombinedWithAnd(string query)
    {
        var sc = Fx.Scenario();
        var result = SearchEngine.Search(sc.Doc, query, AccountFilter.All);

        var account = Assert.Single(result.Accounts);
        Assert.Equal(sc.GoogleSub.Id, account.Id);
    }

    [Fact]
    public void MultipleWords_NoAccountMatchesAll_ReturnsEmpty()
    {
        var sc = Fx.Scenario();
        var result = SearchEngine.Search(sc.Doc, "google ゲーム", AccountFilter.All);

        Assert.Empty(result.Accounts);
        Assert.Empty(result.Services);
    }

    [Fact]
    public void NoMatch_ReturnsEmptyResult()
    {
        var sc = Fx.Scenario();
        var result = SearchEngine.Search(sc.Doc, "存在しない語", AccountFilter.All);

        Assert.Empty(result.Accounts);
        Assert.Empty(result.Services);
    }

    [Fact]
    public void FilterAndQuery_Combine()
    {
        var sc = Fx.Scenario();
        var result = SearchEngine.Search(sc.Doc, "main@example", AccountFilter.EmailOnly);

        var account = Assert.Single(result.Accounts);
        Assert.Equal(sc.GoogleMain.Id, account.Id);
    }

    [Fact]
    public void Search_DoesNotModifyStoredValues()
    {
        var s = Fx.Service("  Mixed Case  ");
        var account = Fx.Direct(s.Id, "User@Example.COM", label: "  Label ");
        var doc = Fx.Doc(new[] { s }, new[] { account });

        var result = SearchEngine.Search(doc, "user@example", AccountFilter.All);

        var found = Assert.Single(result.Accounts);
        Assert.Equal("User@Example.COM", found.LoginIdText);
        Assert.Equal("  Label ", found.Label);
        Assert.Equal("  Mixed Case  ", Assert.Single(result.Services).Name);
    }

    [Fact]
    public void Services_AreOrderedByNameIgnoringCase()
    {
        var sc = Fx.Scenario();
        var result = SearchEngine.Search(sc.Doc, "", AccountFilter.All);

        Assert.Equal(new[] { "Amazon", "Google", "ゲームA" }, result.Services.Select(s => s.Name).ToArray());
    }
}
