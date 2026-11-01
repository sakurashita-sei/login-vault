using LoginVault.Core.Model;
using LoginVault.Core.Validation;

namespace LoginVault.Core.Operations;

/// <summary>
/// Pure operations that produce a new <see cref="VaultDocument"/>. Every result is validated
/// as a whole before it is returned, so the caller can persist it in one step.
/// </summary>
public static class VaultOperations
{
    public static OperationResult AddService(VaultDocument document, string name, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(document);
        var service = new ServiceEntry
        {
            Id = Guid.NewGuid(),
            Name = (name ?? string.Empty).Trim(),
            CreatedAt = now,
            UpdatedAt = now,
        };
        var candidate = document with { Services = Append(document.Services, service) };
        return Finish(document, candidate, service.Id);
    }

    /// <summary>
    /// Resolves the service named <paramref name="serviceName"/> (an existing one is reused, ignoring case and surrounding spaces)
    /// or adds it, then applies <paramref name="next"/> with its ID. Both happen in one result, so a save stores the new service
    /// and the account together or neither (D-048).
    /// </summary>
    public static OperationResult WithService(VaultDocument document, string serviceName, DateTimeOffset now, Func<VaultDocument, Guid, OperationResult> next)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(next);
        if (VaultQueries.FindServiceByName(document, serviceName) is { } existing)
        {
            return next(document, existing.Id);
        }

        var added = AddService(document, serviceName, now);
        if (!added.Succeeded)
        {
            return added;
        }

        var result = next(added.Document, added.CreatedId!.Value);
        if (!result.Succeeded)
        {
            return OperationResult.Failure(document, result.Issues);
        }

        return OperationResult.Success(result.Document, added.Warnings.Concat(result.Warnings).Distinct().ToList(), result.CreatedId);
    }

    public static OperationResult RenameService(VaultDocument document, Guid serviceId, string name, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(document);
        var existing = VaultQueries.FindService(document, serviceId);
        if (existing is null)
        {
            return OperationResult.Failure(document, ValidationIssue.Error(IssueCode.ServiceNotFound, "サービスが見つかりません。", serviceId: serviceId));
        }

        var renamed = existing with { Name = (name ?? string.Empty).Trim(), UpdatedAt = now };
        var candidate = document with { Services = Replace(document.Services, renamed, s => s.Id == serviceId) };
        return Finish(document, candidate);
    }

    /// <summary>Deletes a service and all of its accounts. Refused while accounts of other services reference an email account inside it.</summary>
    public static OperationResult DeleteService(VaultDocument document, Guid serviceId)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (VaultQueries.FindService(document, serviceId) is null)
        {
            return OperationResult.Failure(document, ValidationIssue.Error(IssueCode.ServiceNotFound, "サービスが見つかりません。", serviceId: serviceId));
        }

        var internalIds = document.Accounts.Where(a => a.ServiceId == serviceId).Select(a => a.Id).ToHashSet();
        var externalReferrers = document.Accounts
            .Where(a => a.ServiceId != serviceId && a.LoginEmailAccountId is { } target && internalIds.Contains(target))
            .ToList();
        if (externalReferrers.Count > 0)
        {
            return OperationResult.Failure(
                document,
                ValidationIssue.Error(
                    IssueCode.ServiceHasExternalReferences,
                    $"このサービス内のメールアカウントを、他のサービスの{externalReferrers.Count}件のアカウントがログインIDとして参照しています。先に参照を解除してください。",
                    serviceId: serviceId));
        }

        var candidate = document with
        {
            Services = document.Services.Where(s => s.Id != serviceId).ToList(),
            Accounts = document.Accounts.Where(a => a.ServiceId != serviceId).ToList(),
        };
        return Finish(document, candidate);
    }

    public static OperationResult AddAccount(VaultDocument document, AccountInput input, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(input);
        if (VaultQueries.FindService(document, input.ServiceId) is null)
        {
            return OperationResult.Failure(document, ValidationIssue.Error(IssueCode.UnknownService, "所属サービスが存在しません。", serviceId: input.ServiceId));
        }

        var account = new AccountEntry
        {
            Id = Guid.NewGuid(),
            ServiceId = input.ServiceId,
            Label = (input.Label ?? string.Empty).Trim(),
            LoginIdMode = input.LoginIdMode,
            LoginIdText = input.LoginIdMode == LoginIdMode.Direct ? input.LoginIdText : null,
            LoginEmailAccountId = input.LoginIdMode == LoginIdMode.EmailReference ? input.LoginEmailAccountId : null,
            Password = input.Password.Resolve(string.Empty),
            IsEmailAccount = input.IsEmailAccount,
            MfaStatus = input.MfaStatus,
            MfaMethods = input.MfaMethods.ToList(),
            Notes = input.Notes ?? string.Empty,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var candidate = document with { Accounts = Append(document.Accounts, account) };
        return Finish(document, candidate, account.Id);
    }

    public static OperationResult UpdateAccount(VaultDocument document, Guid accountId, AccountInput input, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(input);
        var existing = VaultQueries.FindAccount(document, accountId);
        if (existing is null)
        {
            return OperationResult.Failure(document, ValidationIssue.Error(IssueCode.AccountNotFound, "アカウントが見つかりません。", accountId: accountId));
        }

        // 参照されているメールアカウントは、参照が残る限り種別や指定方法を変えられない
        var losesEmailRole = existing.IsEmailAccount && (!input.IsEmailAccount || input.LoginIdMode != LoginIdMode.Direct);
        if (losesEmailRole && VaultQueries.ReferrersOf(document, accountId).Count is var referrerCount and > 0)
        {
            return OperationResult.Failure(document, ReferencedIssue(accountId, referrerCount));
        }

        var updated = existing with
        {
            ServiceId = input.ServiceId,
            Label = (input.Label ?? string.Empty).Trim(),
            LoginIdMode = input.LoginIdMode,
            LoginIdText = input.LoginIdMode == LoginIdMode.Direct ? input.LoginIdText : null,
            LoginEmailAccountId = input.LoginIdMode == LoginIdMode.EmailReference ? input.LoginEmailAccountId : null,
            Password = input.Password.Resolve(existing.Password),
            IsEmailAccount = input.IsEmailAccount,
            MfaStatus = input.MfaStatus,
            MfaMethods = input.MfaMethods.ToList(),
            Notes = input.Notes ?? string.Empty,
            UpdatedAt = now,
        };
        var candidate = document with { Accounts = Replace(document.Accounts, updated, a => a.Id == accountId) };
        return Finish(document, candidate);
    }

    public static OperationResult DeleteAccount(VaultDocument document, Guid accountId)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (VaultQueries.FindAccount(document, accountId) is null)
        {
            return OperationResult.Failure(document, ValidationIssue.Error(IssueCode.AccountNotFound, "アカウントが見つかりません。", accountId: accountId));
        }

        var referrerCount = VaultQueries.ReferrersOf(document, accountId).Count;
        if (referrerCount > 0)
        {
            return OperationResult.Failure(document, ReferencedIssue(accountId, referrerCount));
        }

        var candidate = document with { Accounts = document.Accounts.Where(a => a.Id != accountId).ToList() };
        return Finish(document, candidate);
    }

    /// <summary>Turns an email reference into a direct login ID holding the currently resolved address.</summary>
    public static OperationResult ConvertReferenceToDirect(VaultDocument document, Guid accountId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(document);
        var existing = VaultQueries.FindAccount(document, accountId);
        if (existing is null)
        {
            return OperationResult.Failure(document, ValidationIssue.Error(IssueCode.AccountNotFound, "アカウントが見つかりません。", accountId: accountId));
        }

        if (existing.LoginIdMode != LoginIdMode.EmailReference)
        {
            return OperationResult.Failure(document, ValidationIssue.Error(IssueCode.NotAReference, "このアカウントはメール参照ではありません。", accountId: accountId));
        }

        var resolution = VaultQueries.ResolveLoginId(document, existing);
        if (resolution.IsBroken || resolution.Value is null)
        {
            return OperationResult.Failure(document, ValidationIssue.Error(IssueCode.ReferenceTargetMissing, "参照先のメールアカウントが解決できないため変換できません。", accountId: accountId));
        }

        var converted = existing with
        {
            LoginIdMode = LoginIdMode.Direct,
            LoginIdText = resolution.Value,
            LoginEmailAccountId = null,
            UpdatedAt = now,
        };
        var candidate = document with { Accounts = Replace(document.Accounts, converted, a => a.Id == accountId) };
        return Finish(document, candidate);
    }

    /// <summary>Points an email reference at a different email account.</summary>
    public static OperationResult RetargetReference(VaultDocument document, Guid accountId, Guid newEmailAccountId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(document);
        var existing = VaultQueries.FindAccount(document, accountId);
        if (existing is null)
        {
            return OperationResult.Failure(document, ValidationIssue.Error(IssueCode.AccountNotFound, "アカウントが見つかりません。", accountId: accountId));
        }

        if (existing.LoginIdMode != LoginIdMode.EmailReference)
        {
            return OperationResult.Failure(document, ValidationIssue.Error(IssueCode.NotAReference, "このアカウントはメール参照ではありません。", accountId: accountId));
        }

        var retargeted = existing with { LoginEmailAccountId = newEmailAccountId, UpdatedAt = now };
        var candidate = document with { Accounts = Replace(document.Accounts, retargeted, a => a.Id == accountId) };
        return Finish(document, candidate);
    }

    public static OperationResult UpdatePreferences(VaultDocument document, VaultPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(preferences);
        return Finish(document, document with { Preferences = preferences });
    }

    /// <summary>
    /// Applies steps in order. If any step fails the original document is returned, so a
    /// multi-step change (e.g. convert references, then delete) is saved all at once or not at all.
    /// </summary>
    public static OperationResult ApplyAll(VaultDocument document, params Func<VaultDocument, OperationResult>[] steps)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(steps);
        var current = document;
        var warnings = new List<ValidationIssue>();
        Guid? createdId = null;
        foreach (var step in steps)
        {
            var result = step(current);
            if (!result.Succeeded)
            {
                return OperationResult.Failure(document, result.Issues);
            }

            current = result.Document;
            warnings.AddRange(result.Warnings);
            // 複数の手順をまとめても、作成したアカウント・サービスの ID は呼び出し元に伝える
            createdId = result.CreatedId ?? createdId;
        }

        return OperationResult.Success(current, warnings.Distinct().ToList(), createdId);
    }

    private static ValidationIssue ReferencedIssue(Guid accountId, int referrerCount) =>
        ValidationIssue.Error(
            IssueCode.AccountIsReferenced,
            $"このメールアカウントは{referrerCount}件のアカウントからログインIDとして参照されています。先に参照を解除するか、別のメールアカウントに付け替えてください。",
            accountId: accountId);

    private static OperationResult Finish(VaultDocument original, VaultDocument candidate, Guid? createdId = null)
    {
        var report = VaultValidator.Validate(candidate);
        if (report.HasErrors)
        {
            return OperationResult.Failure(original, report.Issues);
        }

        // 変更前から存在した警告は除き、この操作で新たに生じた警告だけを返す
        var before = VaultValidator.Validate(original).Warnings.ToHashSet();
        var newWarnings = report.Warnings.Where(w => !before.Contains(w)).ToList();
        return OperationResult.Success(candidate, newWarnings, createdId);
    }

    private static IReadOnlyList<T> Append<T>(IReadOnlyList<T> list, T item)
    {
        var result = new List<T>(list.Count + 1);
        result.AddRange(list);
        result.Add(item);
        return result;
    }

    private static IReadOnlyList<T> Replace<T>(IReadOnlyList<T> list, T replacement, Func<T, bool> match) =>
        list.Select(item => match(item) ? replacement : item).ToList();
}
