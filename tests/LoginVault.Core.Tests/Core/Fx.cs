using LoginVault.Core.Model;
using LoginVault.Core.Operations;

namespace LoginVault.Core.Tests.Core;

/// <summary>Builds fictitious vault data for tests. Nothing here is real credential data.</summary>
internal static class Fx
{
    public static readonly DateTimeOffset Now = new(2026, 10, 28, 12, 0, 0, TimeSpan.FromHours(9));
    public static readonly DateTimeOffset Later = Now.AddMinutes(5);

    public static ServiceEntry Service(string name, Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Name = name,
        CreatedAt = Now,
        UpdatedAt = Now,
    };

    public static AccountEntry Direct(
        Guid serviceId,
        string? loginId,
        string password = "pw-架空",
        string label = "",
        bool isEmailAccount = false,
        MfaStatus mfaStatus = MfaStatus.Unverified,
        IReadOnlyList<MfaMethod>? methods = null,
        string notes = "",
        Guid? id = null) => new()
        {
            Id = id ?? Guid.NewGuid(),
            ServiceId = serviceId,
            Label = label,
            LoginIdMode = LoginIdMode.Direct,
            LoginIdText = loginId,
            Password = password,
            IsEmailAccount = isEmailAccount,
            MfaStatus = mfaStatus,
            MfaMethods = methods ?? Array.Empty<MfaMethod>(),
            Notes = notes,
            CreatedAt = Now,
            UpdatedAt = Now,
        };

    public static AccountEntry Reference(
        Guid serviceId,
        Guid? emailAccountId,
        string password = "pw-架空",
        string label = "",
        string notes = "",
        bool isEmailAccount = false,
        Guid? id = null) => new()
        {
            Id = id ?? Guid.NewGuid(),
            ServiceId = serviceId,
            Label = label,
            LoginIdMode = LoginIdMode.EmailReference,
            LoginEmailAccountId = emailAccountId,
            Password = password,
            IsEmailAccount = isEmailAccount,
            Notes = notes,
            CreatedAt = Now,
            UpdatedAt = Now,
        };

    public static VaultDocument Doc(IEnumerable<ServiceEntry> services, IEnumerable<AccountEntry> accounts) => new()
    {
        Services = services.ToArray(),
        Accounts = accounts.ToArray(),
    };

    public static AccountInput Input(
        Guid serviceId,
        string? loginId,
        PasswordChange? password = null,
        string label = "",
        bool isEmailAccount = false,
        MfaStatus mfaStatus = MfaStatus.Unverified,
        IReadOnlyList<MfaMethod>? methods = null,
        string notes = "") => new()
        {
            ServiceId = serviceId,
            Label = label,
            LoginIdMode = LoginIdMode.Direct,
            LoginIdText = loginId,
            Password = password ?? PasswordChange.Set("pw-架空"),
            IsEmailAccount = isEmailAccount,
            MfaStatus = mfaStatus,
            MfaMethods = methods ?? Array.Empty<MfaMethod>(),
            Notes = notes,
        };

    public static AccountInput ReferenceInput(Guid serviceId, Guid emailAccountId, PasswordChange? password = null, string label = "") => new()
    {
        ServiceId = serviceId,
        Label = label,
        LoginIdMode = LoginIdMode.EmailReference,
        LoginEmailAccountId = emailAccountId,
        Password = password ?? PasswordChange.Set("pw-架空"),
    };

    /// <summary>A standard fictitious scenario shared by many tests.</summary>
    public sealed record ScenarioData(
        VaultDocument Doc,
        ServiceEntry Google,
        ServiceEntry Amazon,
        ServiceEntry Game,
        AccountEntry GoogleMain,
        AccountEntry GoogleSub,
        AccountEntry AmazonPersonal,
        AccountEntry GamePlayer);

    public static ScenarioData Scenario()
    {
        var google = Service("Google");
        var amazon = Service("Amazon");
        var game = Service("ゲームA");
        var googleMain = Direct(
            google.Id,
            "main@example.com",
            password: "google-main-pw",
            label: "メイン",
            isEmailAccount: true,
            mfaStatus: MfaStatus.Enabled,
            methods: new[]
            {
                new MfaMethod(MfaMethodType.Sms, Detail: "末尾1234"),
                new MfaMethod(MfaMethodType.Authenticator, Detail: "スマホ"),
            });
        var googleSub = Direct(
            google.Id,
            "sub@example.com",
            password: "google-sub-pw",
            label: "サブ",
            isEmailAccount: true,
            mfaStatus: MfaStatus.NotConfigured);
        var amazonPersonal = Reference(amazon.Id, googleMain.Id, password: "amazon-pw", label: "個人用", notes: "プライム会員");
        var gamePlayer = Direct(
            game.Id,
            "player01",
            password: "game-pw",
            label: "メイン",
            mfaStatus: MfaStatus.Enabled,
            methods: new[] { new MfaMethod(MfaMethodType.Other, "YubiKey", "鍵2本") });
        var doc = Doc(new[] { google, amazon, game }, new[] { googleMain, googleSub, amazonPersonal, gamePlayer });
        return new ScenarioData(doc, google, amazon, game, googleMain, googleSub, amazonPersonal, gamePlayer);
    }
}
