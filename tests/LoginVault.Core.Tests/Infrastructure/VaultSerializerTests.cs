using System.Text;
using LoginVault.Core.Model;
using LoginVault.Core.Tests.Core;
using LoginVault.Core.Validation;
using LoginVault.Infrastructure.Serialization;

namespace LoginVault.Core.Tests.Infrastructure;

public class VaultSerializerTests
{
    [Fact]
    public void RoundTrip_PreservesEveryField()
    {
        var sc = Fx.Scenario();
        var bytes = VaultSerializer.Serialize(sc.Doc);
        var restored = VaultSerializer.Deserialize(bytes);

        Assert.Equal(sc.Doc.SchemaVersion, restored.SchemaVersion);
        Assert.Equal(sc.Doc.Preferences, restored.Preferences);
        Assert.Equal(sc.Doc.Services, restored.Services);
        Assert.Equal(sc.Doc.Accounts.Count, restored.Accounts.Count);
        foreach (var (expected, actual) in sc.Doc.Accounts.Zip(restored.Accounts))
        {
            Assert.Equal(expected.Id, actual.Id);
            Assert.Equal(expected.ServiceId, actual.ServiceId);
            Assert.Equal(expected.Label, actual.Label);
            Assert.Equal(expected.LoginIdMode, actual.LoginIdMode);
            Assert.Equal(expected.LoginIdText, actual.LoginIdText);
            Assert.Equal(expected.LoginEmailAccountId, actual.LoginEmailAccountId);
            Assert.Equal(expected.Password, actual.Password);
            Assert.Equal(expected.IsEmailAccount, actual.IsEmailAccount);
            Assert.Equal(expected.MfaStatus, actual.MfaStatus);
            Assert.Equal(expected.MfaMethods, actual.MfaMethods);
            Assert.Equal(expected.Notes, actual.Notes);
            Assert.Equal(expected.CreatedAt, actual.CreatedAt);
            Assert.Equal(expected.UpdatedAt, actual.UpdatedAt);
        }
    }

    [Fact]
    public void RoundTrip_PreservesPasswordWhitespaceAndUnicode()
    {
        var s = Fx.Service("A");
        const string password = "  pässwörd\t日本語 🔑  ";
        var doc = Fx.Doc(new[] { s }, new[] { Fx.Direct(s.Id, "x", password: password) });

        var restored = VaultSerializer.Deserialize(VaultSerializer.Serialize(doc));

        Assert.Equal(password, restored.Accounts[0].Password);
    }

    [Fact]
    public void Deserialize_InvalidJson_ThrowsIntegrityException()
    {
        Assert.Throws<VaultIntegrityException>(() => VaultSerializer.Deserialize(Encoding.UTF8.GetBytes("{ not json")));
    }

    [Fact]
    public void Deserialize_DocumentWithDanglingReference_ThrowsIntegrityException()
    {
        var s = Fx.Service("A");
        var doc = Fx.Doc(new[] { s }, new[] { Fx.Reference(s.Id, Guid.NewGuid()) });
        var bytes = VaultSerializer.Serialize(doc);

        var ex = Assert.Throws<VaultIntegrityException>(() => VaultSerializer.Deserialize(bytes));
        Assert.NotEmpty(ex.Issues);
    }

    [Fact]
    public void Deserialize_UnsupportedSchemaVersion_ThrowsIntegrityException()
    {
        var bytes = VaultSerializer.Serialize(VaultDocument.Empty with { SchemaVersion = 42 });

        Assert.Throws<VaultIntegrityException>(() => VaultSerializer.Deserialize(bytes));
    }

    [Fact]
    public void Deserialize_MissingRequiredField_ThrowsIntegrityException()
    {
        const string json = "{\"schemaVersion\":1,\"services\":[{\"id\":\"00000000-0000-0000-0000-000000000001\"}],\"accounts\":[],\"preferences\":{}}";

        Assert.Throws<VaultIntegrityException>(() => VaultSerializer.Deserialize(Encoding.UTF8.GetBytes(json)));
    }

    [Theory]
    [InlineData("{\"schemaVersion\":1,\"services\":null,\"accounts\":[],\"preferences\":{}}")]
    [InlineData("{\"schemaVersion\":1,\"services\":[],\"accounts\":[null],\"preferences\":{}}")]
    [InlineData("{\"schemaVersion\":1,\"services\":[],\"accounts\":[],\"preferences\":null}")]
    [InlineData("{\"schemaVersion\":1,\"services\":[{\"id\":\"8d7b3b6e-6f0a-4a46-9f4b-2b0c6d1e1a11\",\"name\":null,\"createdAt\":\"2026-01-01T00:00:00+00:00\",\"updatedAt\":\"2026-01-01T00:00:00+00:00\"}],\"accounts\":[],\"preferences\":{}}")]
    public void Deserialize_NullWhereAValueIsRequired_IsAnIntegrityError(string json)
    {
        var ex = Assert.Throws<VaultIntegrityException>(() => VaultSerializer.Deserialize(Encoding.UTF8.GetBytes(json)));

        Assert.Contains(ex.Issues, i => i.Code == IssueCode.MissingValue);
    }

    [Fact]
    public void Deserialize_IntegerEnumValue_IsRejected()
    {
        var json = "{\"schemaVersion\":1,\"services\":[],\"accounts\":[{\"id\":\"8d7b3b6e-6f0a-4a46-9f4b-2b0c6d1e1a11\",\"serviceId\":\"8d7b3b6e-6f0a-4a46-9f4b-2b0c6d1e1a12\",\"loginIdMode\":0,\"loginIdText\":\"x\",\"password\":\"p\",\"createdAt\":\"2026-01-01T00:00:00+00:00\",\"updatedAt\":\"2026-01-01T00:00:00+00:00\"}],\"preferences\":{}}";

        Assert.Throws<VaultIntegrityException>(() => VaultSerializer.Deserialize(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void Deserialize_PreferencesWithoutTheScreenCaptureFlag_DefaultToNotExcluded()
    {
        // 旧版が書いた文書（フラグなし）は「画面キャプチャから除外しない」として読む
        var json = "{\"schemaVersion\":1,\"services\":[],\"accounts\":[],\"preferences\":{\"autoLockMinutes\":5,\"revealSeconds\":15,\"clipboardClearSeconds\":30,\"backupGenerations\":10}}";

        var document = VaultSerializer.Deserialize(Encoding.UTF8.GetBytes(json));

        Assert.False(document.Preferences.ExcludeFromScreenCapture);
    }

    [Fact]
    public void RoundTrip_PreservesTheScreenCaptureChoice()
    {
        var document = VaultDocument.Empty with { Preferences = new VaultPreferences { ExcludeFromScreenCapture = true } };

        var restored = VaultSerializer.Deserialize(VaultSerializer.Serialize(document));

        Assert.True(restored.Preferences.ExcludeFromScreenCapture);
    }

    [Fact]
    public void Deserialize_PreferencesWithoutGeneratorOptions_DefaultToTwelveCharacters()
    {
        var json = "{\"schemaVersion\":1,\"services\":[],\"accounts\":[],\"preferences\":{\"autoLockMinutes\":5,\"revealSeconds\":15,\"clipboardClearSeconds\":30,\"backupGenerations\":10}}";

        var document = VaultSerializer.Deserialize(Encoding.UTF8.GetBytes(json));

        Assert.Equal(new PasswordGeneratorOptions(), document.Preferences.PasswordGenerator);
        Assert.Equal(12, document.Preferences.PasswordGenerator.Length);
    }

    [Fact]
    public void RoundTrip_PreservesGeneratorOptions()
    {
        var options = new PasswordGeneratorOptions { Length = 20, UseSymbols = false, EnabledSymbols = "-_.", ExcludeAmbiguous = true, NoRepeatedCharacters = true };
        var document = VaultDocument.Empty with { Preferences = new VaultPreferences { PasswordGenerator = options } };

        var restored = VaultSerializer.Deserialize(VaultSerializer.Serialize(document));

        Assert.Equal(options, restored.Preferences.PasswordGenerator);
    }

    [Fact]
    public void Deserialize_IgnoresUnknownProperties_SoAVaultSavedByANewerBuildStillOpens()
    {
        // 将来の版が足した項目（設定・アカウント・文書直下）を含む JSON でも、この版は未知の項目を無視して開ける（追加だけの変更なら旧版へ戻しても読める）
        var json = "{\"schemaVersion\":1,\"futureField\":{\"x\":1},\"services\":[],\"accounts\":[],"
            + "\"preferences\":{\"autoLockMinutes\":5,\"revealSeconds\":15,\"clipboardClearSeconds\":30,\"backupGenerations\":10,\"futurePreference\":true,"
            + "\"passwordGenerator\":{\"length\":16,\"futureOption\":\"x\"}}}";

        var document = VaultSerializer.Deserialize(Encoding.UTF8.GetBytes(json));

        Assert.Equal(5, document.Preferences.AutoLockMinutes);
        Assert.Equal(16, document.Preferences.PasswordGenerator.Length);
        Assert.True(document.Preferences.PasswordGenerator.UseSymbols);
    }
}
