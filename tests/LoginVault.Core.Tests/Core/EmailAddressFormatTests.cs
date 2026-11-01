using LoginVault.Core.Validation;

namespace LoginVault.Core.Tests.Core;

public class EmailAddressFormatTests
{
    [Theory]
    [InlineData("main@example.com")]
    [InlineData("first.last+tag@sub.example.co.jp")]
    [InlineData("user_name-1@example.org")]
    [InlineData("ユーザー@例え.jp")]
    public void ValidAddresses_AreAccepted(string value)
    {
        Assert.True(EmailAddressFormat.IsValid(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("plain")]
    [InlineData("a@b")]
    [InlineData("a@b.")]
    [InlineData("@example.com")]
    [InlineData("a@.com")]
    [InlineData("a b@example.com")]
    [InlineData("a@exa mple.com")]
    [InlineData("a@@example.com")]
    [InlineData("a@example.com ")]
    [InlineData("a@-example.com")]
    public void InvalidAddresses_AreRejected(string? value)
    {
        Assert.False(EmailAddressFormat.IsValid(value));
    }
}
