using System.Globalization;
using LoginVault.Core.Model;

namespace LoginVault.Core.Validation;

/// <summary>
/// Checks the integrity rules of a <see cref="VaultDocument"/>. Pure and side-effect free. Item counts and
/// text lengths are bounded by <see cref="VaultLimits"/>, and the report itself is capped, so a crafted
/// document cannot turn validation into a time or memory sink.
/// </summary>
public static class VaultValidator
{
    public static ValidationReport Validate(VaultDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var issues = new IssueCollector();

        if (document.SchemaVersion != VaultDocument.CurrentSchemaVersion)
        {
            issues.Add(ValidationIssue.Error(IssueCode.UnsupportedSchemaVersion, $"未対応のスキーマバージョンです（{document.SchemaVersion}）。"));
        }

        // 復号した JSON に由来する null は整合性エラーとして扱い、例外にしない
        if (document.Preferences is null)
        {
            issues.Add(ValidationIssue.Error(IssueCode.MissingValue, "設定が欠けています。"));
        }
        else if (!document.Preferences.IsValid)
        {
            issues.Add(ValidationIssue.Error(IssueCode.InvalidPreferences, "設定値が許容範囲外です。"));
        }

        if (document.Services is null)
        {
            issues.Add(ValidationIssue.Error(IssueCode.MissingValue, "サービス一覧が欠けています。"));
        }

        if (document.Accounts is null)
        {
            issues.Add(ValidationIssue.Error(IssueCode.MissingValue, "アカウント一覧が欠けています。"));
        }

        if (document.Services is null || document.Accounts is null)
        {
            return issues.ToReport();
        }

        // 件数の上限を超えた文書は個々の検査をしない（細工された巨大な文書で時間とメモリを使い切らないため）
        if (document.Services.Count > VaultLimits.MaxServices)
        {
            issues.Add(ValidationIssue.Error(IssueCode.TooManyItems, $"サービス数が上限（{Format(VaultLimits.MaxServices)} 件）を超えています。"));
        }

        if (document.Accounts.Count > VaultLimits.MaxAccounts)
        {
            issues.Add(ValidationIssue.Error(IssueCode.TooManyItems, $"アカウント数が上限（{Format(VaultLimits.MaxAccounts)} 件）を超えています。"));
        }

        if (document.Services.Count > VaultLimits.MaxServices || document.Accounts.Count > VaultLimits.MaxAccounts)
        {
            return issues.ToReport();
        }

        var serviceIds = ValidateServices(document, issues);
        var accountsById = ValidateAccounts(document, serviceIds, issues);
        WarnDuplicateLoginIds(document, accountsById, issues);

        return issues.ToReport();
    }

    private static HashSet<Guid> ValidateServices(VaultDocument document, IssueCollector issues)
    {
        var ids = new HashSet<Guid>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var service in document.Services)
        {
            if (service is null)
            {
                issues.Add(ValidationIssue.Error(IssueCode.MissingValue, "サービスの項目が欠けています。"));
                continue;
            }

            if (!ids.Add(service.Id))
            {
                issues.Add(ValidationIssue.Error(IssueCode.DuplicateServiceId, "サービスIDが重複しています。", serviceId: service.Id));
            }

            if (service.Name is null)
            {
                issues.Add(ValidationIssue.Error(IssueCode.MissingValue, "サービス名が欠けています。", serviceId: service.Id));
            }
            else if (string.IsNullOrWhiteSpace(service.Name))
            {
                issues.Add(ValidationIssue.Error(IssueCode.ServiceNameRequired, "サービス名は必須です。", serviceId: service.Id));
            }
            else if (!names.Add(service.Name.Trim()))
            {
                issues.Add(ValidationIssue.Warning(IssueCode.DuplicateServiceName, "同じ名前のサービスが既にあります。既存のサービスにアカウントを追加することもできます。", serviceId: service.Id));
            }

            if (service.Name is { Length: > VaultLimits.MaxTextLength })
            {
                issues.Add(ValidationIssue.Error(IssueCode.ValueTooLong, $"サービス名が長すぎます（上限 {Format(VaultLimits.MaxTextLength)} 文字）。", serviceId: service.Id));
            }
        }

        return ids;
    }

    private static Dictionary<Guid, AccountEntry> ValidateAccounts(VaultDocument document, HashSet<Guid> serviceIds, IssueCollector issues)
    {
        var byId = new Dictionary<Guid, AccountEntry>();
        foreach (var account in document.Accounts)
        {
            if (account is null)
            {
                issues.Add(ValidationIssue.Error(IssueCode.MissingValue, "アカウントの項目が欠けています。"));
                continue;
            }

            if (!byId.TryAdd(account.Id, account))
            {
                issues.Add(ValidationIssue.Error(IssueCode.DuplicateAccountId, "アカウントIDが重複しています。", accountId: account.Id));
            }
        }

        foreach (var account in document.Accounts)
        {
            if (account is null)
            {
                continue;
            }

            if (account.Label is null || account.Password is null || account.Notes is null || account.MfaMethods is null)
            {
                issues.Add(ValidationIssue.Error(IssueCode.MissingValue, "アカウントの必須項目が欠けています。", account.ServiceId, account.Id));
            }

            ValidateLengths(account, issues);

            if (!Enum.IsDefined(account.MfaStatus))
            {
                issues.Add(ValidationIssue.Error(IssueCode.InvalidValue, "二段階認証の状態が不正です。", account.ServiceId, account.Id));
            }

            if (!serviceIds.Contains(account.ServiceId))
            {
                issues.Add(ValidationIssue.Error(IssueCode.UnknownService, "所属サービスが存在しません。", account.ServiceId, account.Id));
            }

            switch (account.LoginIdMode)
            {
                case LoginIdMode.Direct:
                    ValidateDirect(account, issues);
                    break;
                case LoginIdMode.EmailReference:
                    ValidateReference(account, byId, issues);
                    break;
                default:
                    issues.Add(ValidationIssue.Error(IssueCode.LoginIdRequired, "ログインIDの指定方法が不正です。", accountId: account.Id));
                    break;
            }

            if (account.MfaMethods is not null)
            {
                ValidateMfa(account, issues);
            }

            if (account.Password is { Length: 0 })
            {
                issues.Add(ValidationIssue.Warning(IssueCode.EmptyPassword, "パスワードが空欄です。", accountId: account.Id));
            }
        }

        return byId;
    }

    private static void ValidateLengths(AccountEntry account, IssueCollector issues)
    {
        if (account.Label is { Length: > VaultLimits.MaxTextLength })
        {
            issues.Add(ValidationIssue.Error(IssueCode.ValueTooLong, $"アカウント表示名が長すぎます（上限 {Format(VaultLimits.MaxTextLength)} 文字）。", account.ServiceId, account.Id));
        }

        if (account.LoginIdText is { Length: > VaultLimits.MaxTextLength })
        {
            issues.Add(ValidationIssue.Error(IssueCode.ValueTooLong, $"ログインIDが長すぎます（上限 {Format(VaultLimits.MaxTextLength)} 文字）。", account.ServiceId, account.Id));
        }

        if (account.Password is { Length: > VaultLimits.MaxPasswordLength })
        {
            issues.Add(ValidationIssue.Error(IssueCode.ValueTooLong, $"パスワードが長すぎます（上限 {Format(VaultLimits.MaxPasswordLength)} 文字）。", account.ServiceId, account.Id));
        }

        if (account.Notes is { Length: > VaultLimits.MaxNotesLength })
        {
            issues.Add(ValidationIssue.Error(IssueCode.ValueTooLong, $"メモが長すぎます（上限 {Format(VaultLimits.MaxNotesLength)} 文字）。", account.ServiceId, account.Id));
        }
    }

    private static void ValidateDirect(AccountEntry account, IssueCollector issues)
    {
        if (string.IsNullOrWhiteSpace(account.LoginIdText))
        {
            issues.Add(ValidationIssue.Error(IssueCode.LoginIdRequired, "ログインIDは必須です。", accountId: account.Id));
        }
        else if (account.IsEmailAccount && !EmailAddressFormat.IsValid(account.LoginIdText))
        {
            issues.Add(ValidationIssue.Error(IssueCode.InvalidEmailFormat, "メールアカウントのログインIDはメールアドレスの形式である必要があります。", accountId: account.Id));
        }

        if (account.LoginEmailAccountId is not null)
        {
            issues.Add(ValidationIssue.Error(IssueCode.ReferenceNotAllowedForDirect, "直接入力のアカウントにメール参照は設定できません。", accountId: account.Id));
        }
    }

    private static void ValidateReference(AccountEntry account, Dictionary<Guid, AccountEntry> byId, IssueCollector issues)
    {
        if (account.IsEmailAccount)
        {
            issues.Add(ValidationIssue.Error(IssueCode.EmailAccountMustBeDirect, "メールアカウント自身のログインIDは直接入力にしてください。", accountId: account.Id));
        }

        if (!string.IsNullOrEmpty(account.LoginIdText))
        {
            issues.Add(ValidationIssue.Error(IssueCode.LoginIdTextNotAllowedForReference, "メール参照のアカウントに直接入力のIDは保持できません。", accountId: account.Id));
        }

        if (account.LoginEmailAccountId is not { } targetId)
        {
            issues.Add(ValidationIssue.Error(IssueCode.ReferenceRequired, "参照するメールアカウントを選択してください。", accountId: account.Id));
            return;
        }

        if (targetId == account.Id)
        {
            issues.Add(ValidationIssue.Error(IssueCode.SelfReference, "自分自身を参照することはできません。", accountId: account.Id));
            return;
        }

        if (!byId.TryGetValue(targetId, out var target))
        {
            issues.Add(ValidationIssue.Error(IssueCode.ReferenceTargetMissing, "参照先のメールアカウントが存在しません。", accountId: account.Id));
            return;
        }

        if (!target.IsEmailAccount)
        {
            issues.Add(ValidationIssue.Error(IssueCode.ReferenceTargetNotEmailAccount, "参照先はメールアカウントとして登録された情報である必要があります。", accountId: account.Id));
        }

        if (target.LoginIdMode != LoginIdMode.Direct)
        {
            issues.Add(ValidationIssue.Error(IssueCode.ReferenceTargetNotDirect, "参照先のメールアカウントは直接入力である必要があります（参照の連鎖・循環は許可されません）。", accountId: account.Id));
        }
    }

    private static void ValidateMfa(AccountEntry account, IssueCollector issues)
    {
        if (account.MfaMethods.Count > VaultLimits.MaxMfaMethods)
        {
            issues.Add(ValidationIssue.Error(IssueCode.TooManyItems, $"二段階認証の方式が多すぎます（上限 {VaultLimits.MaxMfaMethods} 件）。", accountId: account.Id));
            return;
        }

        if (account.MfaStatus == MfaStatus.Enabled && account.MfaMethods.Count == 0)
        {
            issues.Add(ValidationIssue.Error(IssueCode.MfaMethodsRequiredWhenEnabled, "二段階認証が「設定あり」の場合は方式を1つ以上選択してください。", accountId: account.Id));
        }

        if (account.MfaStatus != MfaStatus.Enabled && account.MfaMethods.Count > 0)
        {
            issues.Add(ValidationIssue.Error(IssueCode.MfaMethodsMustBeEmptyWhenNotEnabled, "二段階認証が「設定あり」以外の場合、方式は空にしてください。", accountId: account.Id));
        }

        foreach (var method in account.MfaMethods)
        {
            if (method is null)
            {
                issues.Add(ValidationIssue.Error(IssueCode.MissingValue, "二段階認証の方式の項目が欠けています。", accountId: account.Id));
                continue;
            }

            if (!Enum.IsDefined(method.Type))
            {
                issues.Add(ValidationIssue.Error(IssueCode.InvalidValue, "二段階認証の方式が不正です。", accountId: account.Id));
            }

            if (method.Type == MfaMethodType.Other && string.IsNullOrWhiteSpace(method.CustomType))
            {
                issues.Add(ValidationIssue.Error(IssueCode.OtherMfaMethodNeedsCustomType, "方式「その他」には方式名を入力してください。", accountId: account.Id));
            }

            if (method.CustomType is { Length: > VaultLimits.MaxTextLength } || method.Detail is { Length: > VaultLimits.MaxTextLength })
            {
                issues.Add(ValidationIssue.Error(IssueCode.ValueTooLong, $"二段階認証の方式名または補足が長すぎます（上限 {Format(VaultLimits.MaxTextLength)} 文字）。", accountId: account.Id));
            }
        }
    }

    private static void WarnDuplicateLoginIds(VaultDocument document, Dictionary<Guid, AccountEntry> byId, IssueCollector issues)
    {
        // サービスごとに、解決後のログインIDを大文字小文字を無視して比較する
        var seen = new HashSet<(Guid ServiceId, string LoginId)>(new ServiceLoginIdComparer());
        foreach (var account in document.Accounts)
        {
            if (account is null)
            {
                continue;
            }

            var loginId = ResolveForComparison(account, byId);
            if (loginId is null)
            {
                continue;
            }

            if (!seen.Add((account.ServiceId, loginId)))
            {
                issues.Add(ValidationIssue.Warning(IssueCode.DuplicateLoginIdInService, "同じサービス内に同じログインIDのアカウントが既にあります。", account.ServiceId, account.Id));
            }
        }
    }

    private static string? ResolveForComparison(AccountEntry account, Dictionary<Guid, AccountEntry> byId)
    {
        if (account.LoginIdMode == LoginIdMode.Direct)
        {
            return string.IsNullOrWhiteSpace(account.LoginIdText) ? null : account.LoginIdText;
        }

        if (account.LoginEmailAccountId is { } targetId && byId.TryGetValue(targetId, out var target) && target.LoginIdMode == LoginIdMode.Direct)
        {
            return string.IsNullOrWhiteSpace(target.LoginIdText) ? null : target.LoginIdText;
        }

        return null;
    }

    private static string Format(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>Collects issues up to <see cref="VaultLimits.MaxReportedIssues"/>; a document with millions of faults cannot grow the report without bound.</summary>
    private sealed class IssueCollector
    {
        private readonly List<ValidationIssue> _issues = new();
        private bool _truncated;

        public void Add(ValidationIssue issue)
        {
            if (_issues.Count < VaultLimits.MaxReportedIssues)
            {
                _issues.Add(issue);
            }
            else if (!_truncated)
            {
                _truncated = true;
                _issues.Add(ValidationIssue.Error(IssueCode.TooManyIssues, "問題が多すぎるため、以降の所見は省略しました。"));
            }
        }

        public ValidationReport ToReport() => new(_issues);
    }

    private sealed class ServiceLoginIdComparer : IEqualityComparer<(Guid ServiceId, string LoginId)>
    {
        public bool Equals((Guid ServiceId, string LoginId) x, (Guid ServiceId, string LoginId) y) =>
            x.ServiceId == y.ServiceId && string.Equals(x.LoginId, y.LoginId, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((Guid ServiceId, string LoginId) obj) =>
            HashCode.Combine(obj.ServiceId, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.LoginId));
    }
}
