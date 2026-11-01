using LoginVault.Core.Model;
using LoginVault.Core.Operations;
using LoginVault.Infrastructure.Crypto;
using LoginVault.Infrastructure.Session;

namespace LoginVault.Core.Tests.Core;

public class ModelSafetyTests
{
    [Fact]
    public void AccountEntry_ToString_DoesNotRevealPasswordOrNotes()
    {
        var sc = Fx.Scenario();
        var text = sc.AmazonPersonal.ToString();

        Assert.DoesNotContain("amazon-pw", text);
        Assert.DoesNotContain("プライム会員", text);
    }

    [Fact]
    public void VaultDocument_ToString_DoesNotRevealSecrets()
    {
        var sc = Fx.Scenario();
        var text = sc.Doc.ToString();

        Assert.DoesNotContain("amazon-pw", text);
        Assert.DoesNotContain("main@example.com", text);
    }

    [Fact]
    public void PasswordChangeSet_ToString_DoesNotRevealValue()
    {
        var change = PasswordChange.Set("top-secret-架空");

        Assert.DoesNotContain("top-secret", change.ToString());
    }

    [Fact]
    public void AccountInput_ToString_DoesNotRevealPassword()
    {
        var input = Fx.Input(Guid.NewGuid(), "x", PasswordChange.Set("top-secret-架空"), notes: "秘密メモ");

        Assert.DoesNotContain("top-secret", input.ToString());
        Assert.DoesNotContain("秘密メモ", input.ToString());
    }

    [Theory]
    [InlineData(MfaStatus.Unverified, "未設定")]
    [InlineData(MfaStatus.NotConfigured, "未設定")]
    [InlineData(MfaStatus.Enabled, "設定あり")]
    public void MfaStatus_DisplayText(MfaStatus status, string expected)
    {
        Assert.Equal(expected, DisplayText.Of(status));
    }

    [Theory]
    [InlineData(MfaMethodType.Sms, "SMS")]
    [InlineData(MfaMethodType.Email, "メール")]
    [InlineData(MfaMethodType.Authenticator, "Authenticator")]
    [InlineData(MfaMethodType.Other, "その他")]
    public void MfaMethodType_DisplayText(MfaMethodType type, string expected)
    {
        Assert.Equal(expected, DisplayText.Of(type));
    }

    [Fact]
    public void MfaMethod_Summary_IncludesCustomTypeAndDetail()
    {
        Assert.Equal("その他（YubiKey）：鍵2本", DisplayText.Summary(new MfaMethod(MfaMethodType.Other, "YubiKey", "鍵2本")));
        Assert.Equal("SMS：末尾1234", DisplayText.Summary(new MfaMethod(MfaMethodType.Sms, Detail: "末尾1234")));
        Assert.Equal("メール", DisplayText.Summary(new MfaMethod(MfaMethodType.Email)));
    }

    [Fact]
    public void LoginIdResolution_ToString_DoesNotRevealValue()
    {
        var resolution = LoginIdResolution.Resolved("secret.login@example.test");

        Assert.DoesNotContain("secret.login", resolution.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ServiceEntry_ToString_DoesNotRevealName()
    {
        var service = Fx.Service("SecretServiceName架空");

        Assert.DoesNotContain("SecretServiceName", service.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void MfaMethod_ToString_DoesNotRevealDetailOrCustomType()
    {
        var method = new MfaMethod(MfaMethodType.Other, CustomType: "独自方式架空", Detail: "末尾9999架空");

        Assert.DoesNotContain("独自方式", method.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("9999", method.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void VaultSnapshot_KdfParameters_AndVaultKey_ToString_DoNotRevealContent()
    {
        var sc = Fx.Scenario();
        var snapshot = new VaultSnapshot(Guid.NewGuid(), sc.Doc);
        Assert.DoesNotContain("amazon-pw", snapshot.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("main@example.com", snapshot.ToString(), StringComparison.Ordinal);

        var kdf = KdfParameters.CreateDefault();
        Assert.DoesNotContain(Convert.ToHexString(kdf.Salt), kdf.ToString(), StringComparison.OrdinalIgnoreCase);

        using var key = VaultKey.Derive("テスト用パスワード架空", kdf);
        Assert.Equal("VaultKey", key.ToString());
    }
}
