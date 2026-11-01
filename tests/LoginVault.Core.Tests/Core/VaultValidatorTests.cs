using LoginVault.Core.Model;
using LoginVault.Core.Validation;

namespace LoginVault.Core.Tests.Core;

public class VaultValidatorTests
{
    private static IReadOnlyList<IssueCode> ErrorCodes(VaultDocument doc) =>
        VaultValidator.Validate(doc).Errors.Select(i => i.Code).ToList();

    private static IReadOnlyList<IssueCode> WarningCodes(VaultDocument doc) =>
        VaultValidator.Validate(doc).Warnings.Select(i => i.Code).ToList();

    [Fact]
    public void EmptyDocument_HasNoIssues()
    {
        var report = VaultValidator.Validate(VaultDocument.Empty);

        Assert.False(report.HasErrors);
        Assert.Empty(report.Issues);
    }

    [Fact]
    public void Scenario_HasNoErrors()
    {
        var report = VaultValidator.Validate(Fx.Scenario().Doc);

        Assert.False(report.HasErrors);
    }

    [Fact]
    public void DuplicateServiceId_IsError()
    {
        var id = Guid.NewGuid();
        var doc = Fx.Doc(new[] { Fx.Service("A", id), Fx.Service("B", id) }, Array.Empty<AccountEntry>());

        Assert.Contains(IssueCode.DuplicateServiceId, ErrorCodes(doc));
    }

    [Fact]
    public void DuplicateAccountId_IsError()
    {
        var s = Fx.Service("A");
        var id = Guid.NewGuid();
        var doc = Fx.Doc(new[] { s }, new[] { Fx.Direct(s.Id, "x", id: id), Fx.Direct(s.Id, "y", id: id) });

        Assert.Contains(IssueCode.DuplicateAccountId, ErrorCodes(doc));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void BlankServiceName_IsError(string name)
    {
        var doc = Fx.Doc(new[] { Fx.Service(name) }, Array.Empty<AccountEntry>());

        Assert.Contains(IssueCode.ServiceNameRequired, ErrorCodes(doc));
    }

    [Fact]
    public void DuplicateServiceName_IsWarningNotError()
    {
        var doc = Fx.Doc(new[] { Fx.Service("Google"), Fx.Service("google") }, Array.Empty<AccountEntry>());
        var report = VaultValidator.Validate(doc);

        Assert.False(report.HasErrors);
        Assert.Contains(IssueCode.DuplicateServiceName, WarningCodes(doc));
    }

    [Fact]
    public void AccountWithUnknownService_IsError()
    {
        var doc = Fx.Doc(Array.Empty<ServiceEntry>(), new[] { Fx.Direct(Guid.NewGuid(), "x") });

        Assert.Contains(IssueCode.UnknownService, ErrorCodes(doc));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void DirectAccountWithoutLoginId_IsError(string? loginId)
    {
        var s = Fx.Service("A");
        var doc = Fx.Doc(new[] { s }, new[] { Fx.Direct(s.Id, loginId) });

        Assert.Contains(IssueCode.LoginIdRequired, ErrorCodes(doc));
    }

    [Fact]
    public void DirectAccountWithReferenceId_IsError()
    {
        var s = Fx.Service("A");
        var email = Fx.Direct(s.Id, "a@example.com", isEmailAccount: true);
        var bad = Fx.Direct(s.Id, "x") with { LoginEmailAccountId = email.Id };
        var doc = Fx.Doc(new[] { s }, new[] { email, bad });

        Assert.Contains(IssueCode.ReferenceNotAllowedForDirect, ErrorCodes(doc));
    }

    [Fact]
    public void ReferenceAccountWithoutTargetId_IsError()
    {
        var s = Fx.Service("A");
        var doc = Fx.Doc(new[] { s }, new[] { Fx.Reference(s.Id, null) });

        Assert.Contains(IssueCode.ReferenceRequired, ErrorCodes(doc));
    }

    [Fact]
    public void ReferenceAccountWithLoginIdText_IsError()
    {
        var s = Fx.Service("A");
        var email = Fx.Direct(s.Id, "a@example.com", isEmailAccount: true);
        var bad = Fx.Reference(s.Id, email.Id) with { LoginIdText = "stale@example.com" };
        var doc = Fx.Doc(new[] { s }, new[] { email, bad });

        Assert.Contains(IssueCode.LoginIdTextNotAllowedForReference, ErrorCodes(doc));
    }

    [Fact]
    public void ReferenceToMissingAccount_IsError()
    {
        var s = Fx.Service("A");
        var doc = Fx.Doc(new[] { s }, new[] { Fx.Reference(s.Id, Guid.NewGuid()) });

        Assert.Contains(IssueCode.ReferenceTargetMissing, ErrorCodes(doc));
    }

    [Fact]
    public void ReferenceToAccountThatIsNotEmailAccount_IsError_EvenIfIdLooksLikeEmail()
    {
        var s = Fx.Service("Amazon");
        var amazon = Fx.Direct(s.Id, "me@example.com", isEmailAccount: false);
        var doc = Fx.Doc(new[] { s }, new[] { amazon, Fx.Reference(s.Id, amazon.Id) });

        Assert.Contains(IssueCode.ReferenceTargetNotEmailAccount, ErrorCodes(doc));
    }

    [Fact]
    public void ReferenceChain_TargetNotDirect_IsError()
    {
        var s = Fx.Service("A");
        var email = Fx.Direct(s.Id, "a@example.com", isEmailAccount: true);
        var middle = Fx.Reference(s.Id, email.Id, isEmailAccount: true);
        var last = Fx.Reference(s.Id, middle.Id);
        var doc = Fx.Doc(new[] { s }, new[] { email, middle, last });

        Assert.Contains(IssueCode.ReferenceTargetNotDirect, ErrorCodes(doc));
    }

    [Fact]
    public void SelfReference_IsError()
    {
        var s = Fx.Service("A");
        var id = Guid.NewGuid();
        var self = Fx.Reference(s.Id, id, isEmailAccount: true, id: id);
        var doc = Fx.Doc(new[] { s }, new[] { self });

        Assert.Contains(IssueCode.SelfReference, ErrorCodes(doc));
    }

    [Fact]
    public void CircularReference_IsError()
    {
        var s = Fx.Service("A");
        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();
        var a = Fx.Reference(s.Id, idB, isEmailAccount: true, id: idA);
        var b = Fx.Reference(s.Id, idA, isEmailAccount: true, id: idB);
        var doc = Fx.Doc(new[] { s }, new[] { a, b });

        Assert.Contains(IssueCode.ReferenceTargetNotDirect, ErrorCodes(doc));
    }

    [Fact]
    public void MutualReference_BetweenNonEmailAccounts_IsError()
    {
        var s = Fx.Service("A");
        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();
        var a = Fx.Reference(s.Id, idB, isEmailAccount: false, id: idA);
        var b = Fx.Reference(s.Id, idA, isEmailAccount: false, id: idB);
        var codes = ErrorCodes(Fx.Doc(new[] { s }, new[] { a, b }));

        Assert.Contains(IssueCode.ReferenceTargetNotEmailAccount, codes);
        Assert.Contains(IssueCode.ReferenceTargetNotDirect, codes);
    }

    [Fact]
    public void EmailAccountUsingReferenceMode_IsError()
    {
        var s = Fx.Service("A");
        var email = Fx.Direct(s.Id, "a@example.com", isEmailAccount: true);
        var bad = Fx.Reference(s.Id, email.Id, isEmailAccount: true);
        var doc = Fx.Doc(new[] { s }, new[] { email, bad });

        Assert.Contains(IssueCode.EmailAccountMustBeDirect, ErrorCodes(doc));
    }

    [Theory]
    [InlineData("notanemail")]
    [InlineData("a@b")]
    [InlineData(" lead@example.com")]
    [InlineData("a@@example.com")]
    public void EmailAccountWithInvalidAddress_IsError(string loginId)
    {
        var s = Fx.Service("Mail");
        var doc = Fx.Doc(new[] { s }, new[] { Fx.Direct(s.Id, loginId, isEmailAccount: true) });

        Assert.Contains(IssueCode.InvalidEmailFormat, ErrorCodes(doc));
    }

    [Fact]
    public void NonEmailAccountWithArbitraryId_IsValid()
    {
        var s = Fx.Service("ゲームA");
        var doc = Fx.Doc(new[] { s }, new[] { Fx.Direct(s.Id, "player01") });

        Assert.False(VaultValidator.Validate(doc).HasErrors);
    }

    [Fact]
    public void MfaEnabledWithoutMethods_IsError()
    {
        var s = Fx.Service("A");
        var doc = Fx.Doc(new[] { s }, new[] { Fx.Direct(s.Id, "x", mfaStatus: MfaStatus.Enabled) });

        Assert.Contains(IssueCode.MfaMethodsRequiredWhenEnabled, ErrorCodes(doc));
    }

    [Theory]
    [InlineData(MfaStatus.Unverified)]
    [InlineData(MfaStatus.NotConfigured)]
    public void MfaNotEnabledWithMethods_IsError(MfaStatus status)
    {
        var s = Fx.Service("A");
        var methods = new[] { new MfaMethod(MfaMethodType.Sms) };
        var doc = Fx.Doc(new[] { s }, new[] { Fx.Direct(s.Id, "x", mfaStatus: status, methods: methods) });

        Assert.Contains(IssueCode.MfaMethodsMustBeEmptyWhenNotEnabled, ErrorCodes(doc));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void OtherMfaMethodWithoutCustomType_IsError(string? customType)
    {
        var s = Fx.Service("A");
        var methods = new[] { new MfaMethod(MfaMethodType.Other, customType) };
        var doc = Fx.Doc(new[] { s }, new[] { Fx.Direct(s.Id, "x", mfaStatus: MfaStatus.Enabled, methods: methods) });

        Assert.Contains(IssueCode.OtherMfaMethodNeedsCustomType, ErrorCodes(doc));
    }

    [Fact]
    public void DuplicateLoginIdInSameService_IsWarning()
    {
        var s = Fx.Service("A");
        var doc = Fx.Doc(new[] { s }, new[] { Fx.Direct(s.Id, "Same@Example.com"), Fx.Direct(s.Id, "same@example.com") });
        var report = VaultValidator.Validate(doc);

        Assert.False(report.HasErrors);
        Assert.Contains(IssueCode.DuplicateLoginIdInService, WarningCodes(doc));
    }

    [Fact]
    public void SameLoginIdAcrossServices_IsAllowedWithoutWarning()
    {
        var a = Fx.Service("A");
        var b = Fx.Service("B");
        var doc = Fx.Doc(new[] { a, b }, new[] { Fx.Direct(a.Id, "me@example.com"), Fx.Direct(b.Id, "me@example.com") });

        Assert.DoesNotContain(IssueCode.DuplicateLoginIdInService, WarningCodes(doc));
    }

    [Fact]
    public void EmptyPassword_IsWarning()
    {
        var s = Fx.Service("A");
        var doc = Fx.Doc(new[] { s }, new[] { Fx.Direct(s.Id, "x", password: "") });
        var report = VaultValidator.Validate(doc);

        Assert.False(report.HasErrors);
        Assert.Contains(IssueCode.EmptyPassword, WarningCodes(doc));
    }

    [Fact]
    public void UnsupportedSchemaVersion_IsError()
    {
        var doc = VaultDocument.Empty with { SchemaVersion = 99 };

        Assert.Contains(IssueCode.UnsupportedSchemaVersion, ErrorCodes(doc));
    }

    [Theory]
    [InlineData(0, 15, 30, 10)]
    [InlineData(5, 0, 30, 10)]
    [InlineData(5, 15, 0, 10)]
    [InlineData(5, 15, 30, 0)]
    [InlineData(5, 15, 30, 1000)]
    public void PreferencesOutOfRange_IsError(int lockMinutes, int revealSeconds, int clearSeconds, int generations)
    {
        var prefs = new VaultPreferences
        {
            AutoLockMinutes = lockMinutes,
            RevealSeconds = revealSeconds,
            ClipboardClearSeconds = clearSeconds,
            BackupGenerations = generations,
        };
        var doc = VaultDocument.Empty with { Preferences = prefs };

        Assert.Contains(IssueCode.InvalidPreferences, ErrorCodes(doc));
    }

    [Fact]
    public void IssueCarriesAccountId()
    {
        var s = Fx.Service("A");
        var account = Fx.Direct(s.Id, null);
        var doc = Fx.Doc(new[] { s }, new[] { account });
        var issue = VaultValidator.Validate(doc).Errors.Single(i => i.Code == IssueCode.LoginIdRequired);

        Assert.Equal(account.Id, issue.AccountId);
    }

    [Theory]
    [InlineData(60, 120, 300, 50, true)]
    [InlineData(1, 5, 5, 1, true)]
    [InlineData(61, 120, 300, 50, false)]
    [InlineData(60, 121, 300, 50, false)]
    [InlineData(60, 120, 301, 50, false)]
    [InlineData(60, 120, 300, 51, false)]
    public void PreferencesAtTheBounds_AreAccepted_AndJustOutsideAreRejected(int lockMinutes, int revealSeconds, int clearSeconds, int generations, bool valid)
    {
        var prefs = new VaultPreferences
        {
            AutoLockMinutes = lockMinutes,
            RevealSeconds = revealSeconds,
            ClipboardClearSeconds = clearSeconds,
            BackupGenerations = generations,
        };
        var doc = VaultDocument.Empty with { Preferences = prefs };

        Assert.Equal(!valid, ErrorCodes(doc).Contains(IssueCode.InvalidPreferences));
    }

    [Fact]
    public void NullCollectionsAndStrings_AreReportedAsErrors_NotCrashes()
    {
        var sc = Fx.Scenario();
        var account = sc.AmazonPersonal with { Password = null!, Notes = null!, MfaMethods = null! };
        var documents = new[]
        {
            VaultDocument.Empty with { Services = null! },
            VaultDocument.Empty with { Accounts = null! },
            VaultDocument.Empty with { Preferences = null! },
            VaultDocument.Empty with { Services = new ServiceEntry[] { null! } },
            VaultDocument.Empty with { Accounts = new AccountEntry[] { null! } },
            VaultDocument.Empty with { Services = new[] { Fx.Service("A") with { Name = null! } } },
            sc.Doc with { Accounts = sc.Doc.Accounts.Select(a => a.Id == account.Id ? account : a).ToList() },
        };

        foreach (var doc in documents)
        {
            var report = VaultValidator.Validate(doc);
            Assert.True(report.HasErrors);
            Assert.Contains(report.Errors, i => i.Code == IssueCode.MissingValue);
        }
    }

    [Fact]
    public void UndefinedEnumValues_AreErrors()
    {
        var sc = Fx.Scenario();
        var badStatus = sc.AmazonPersonal with { MfaStatus = (MfaStatus)99, MfaMethods = Array.Empty<MfaMethod>() };
        var badMethod = sc.AmazonPersonal with { MfaStatus = MfaStatus.Enabled, MfaMethods = new[] { new MfaMethod((MfaMethodType)42) } };
        var withBadStatus = sc.Doc with { Accounts = sc.Doc.Accounts.Select(a => a.Id == badStatus.Id ? badStatus : a).ToList() };
        var withBadMethod = sc.Doc with { Accounts = sc.Doc.Accounts.Select(a => a.Id == badMethod.Id ? badMethod : a).ToList() };

        Assert.Contains(IssueCode.InvalidValue, ErrorCodes(withBadStatus));
        Assert.Contains(IssueCode.InvalidValue, ErrorCodes(withBadMethod));
    }

    [Fact]
    public void TooManyServicesOrAccounts_IsError()
    {
        var services = Enumerable.Range(0, VaultLimits.MaxServices + 1).Select(i => Fx.Service("S" + i)).ToList();
        Assert.Contains(IssueCode.TooManyItems, ErrorCodes(Fx.Doc(services, Array.Empty<AccountEntry>())));

        var service = Fx.Service("S");
        var accounts = Enumerable.Range(0, VaultLimits.MaxAccounts + 1).Select(i => Fx.Direct(service.Id, "u" + i)).ToList();
        Assert.Contains(IssueCode.TooManyItems, ErrorCodes(Fx.Doc(new[] { service }, accounts)));
    }

    [Fact]
    public void ExactlyTheMaximumNumberOfItems_IsAccepted()
    {
        var services = Enumerable.Range(0, VaultLimits.MaxServices).Select(i => Fx.Service("S" + i)).ToList();
        var accounts = Enumerable.Range(0, VaultLimits.MaxAccounts).Select(i => Fx.Direct(services[i % services.Count].Id, "u" + i)).ToList();

        Assert.False(VaultValidator.Validate(Fx.Doc(services, accounts)).HasErrors);
    }

    [Fact]
    public void TooManyMfaMethods_IsError()
    {
        var service = Fx.Service("S");
        var methods = Enumerable.Range(0, VaultLimits.MaxMfaMethods + 1).Select(_ => new MfaMethod(MfaMethodType.Sms)).ToList();
        var doc = Fx.Doc(new[] { service }, new[] { Fx.Direct(service.Id, "u", mfaStatus: MfaStatus.Enabled, methods: methods) });

        Assert.Contains(IssueCode.TooManyItems, ErrorCodes(doc));
    }

    [Fact]
    public void TextLengthLimits_AreEnforced_PerField()
    {
        var service = Fx.Service("S");
        var longText = new string('x', VaultLimits.MaxTextLength + 1);
        var cases = new (string Field, VaultDocument Doc)[]
        {
            ("service name", Fx.Doc(new[] { Fx.Service(longText) }, Array.Empty<AccountEntry>())),
            ("label", Fx.Doc(new[] { service }, new[] { Fx.Direct(service.Id, "u", label: longText) })),
            ("login id", Fx.Doc(new[] { service }, new[] { Fx.Direct(service.Id, longText) })),
            ("password", Fx.Doc(new[] { service }, new[] { Fx.Direct(service.Id, "u", password: new string('x', VaultLimits.MaxPasswordLength + 1)) })),
            ("notes", Fx.Doc(new[] { service }, new[] { Fx.Direct(service.Id, "u", notes: new string('x', VaultLimits.MaxNotesLength + 1)) })),
            ("mfa custom type", Fx.Doc(new[] { service }, new[] { Fx.Direct(service.Id, "u", mfaStatus: MfaStatus.Enabled, methods: new[] { new MfaMethod(MfaMethodType.Other, longText) }) })),
            ("mfa detail", Fx.Doc(new[] { service }, new[] { Fx.Direct(service.Id, "u", mfaStatus: MfaStatus.Enabled, methods: new[] { new MfaMethod(MfaMethodType.Sms, Detail: longText) }) })),
        };

        foreach (var (field, doc) in cases)
        {
            Assert.True(ErrorCodes(doc).Contains(IssueCode.ValueTooLong), field);
        }
    }

    [Fact]
    public void TextExactlyAtTheLimit_IsAccepted()
    {
        var service = Fx.Service(new string('s', VaultLimits.MaxTextLength));
        var methods = Enumerable.Range(0, VaultLimits.MaxMfaMethods)
            .Select(_ => new MfaMethod(MfaMethodType.Other, new string('c', VaultLimits.MaxTextLength), new string('d', VaultLimits.MaxTextLength)))
            .ToList();
        var account = Fx.Direct(
            service.Id,
            new string('u', VaultLimits.MaxTextLength),
            password: new string('p', VaultLimits.MaxPasswordLength),
            label: new string('l', VaultLimits.MaxTextLength),
            notes: new string('n', VaultLimits.MaxNotesLength),
            mfaStatus: MfaStatus.Enabled,
            methods: methods);

        Assert.False(VaultValidator.Validate(Fx.Doc(new[] { service }, new[] { account })).HasErrors);
    }

    [Fact]
    public void IssueCollection_StopsAtTheReportingLimit()
    {
        // null 要素を大量に含む細工された文書でも、所見の件数（メモリ）は上限で頭打ちになる
        var accounts = new AccountEntry[VaultLimits.MaxAccounts];
        var report = VaultValidator.Validate(VaultDocument.Empty with { Accounts = accounts });

        Assert.True(report.HasErrors);
        Assert.InRange(report.Issues.Count, 1, VaultLimits.MaxReportedIssues + 1);
        Assert.Contains(IssueCode.TooManyIssues, report.Errors.Select(i => i.Code));
    }

    [Fact]
    public void InvalidOrMissingPasswordGeneratorOptions_AreAPreferencesError()
    {
        var tooShort = VaultDocument.Empty with { Preferences = new VaultPreferences { PasswordGenerator = new PasswordGeneratorOptions { Length = 3 } } };
        var missing = VaultDocument.Empty with { Preferences = new VaultPreferences { PasswordGenerator = null! } };

        Assert.Contains(IssueCode.InvalidPreferences, ErrorCodes(tooShort));
        Assert.Contains(IssueCode.InvalidPreferences, ErrorCodes(missing));
        Assert.False(VaultValidator.Validate(VaultDocument.Empty).HasErrors);
    }
}
