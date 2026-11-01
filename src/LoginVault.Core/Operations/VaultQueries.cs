using LoginVault.Core.Model;

namespace LoginVault.Core.Operations;

/// <summary>Result of resolving an account's effective login ID.</summary>
public sealed record LoginIdResolution(string? Value, bool IsBroken)
{
    public static LoginIdResolution Broken { get; } = new(null, true);

    public static LoginIdResolution Resolved(string value) => new(value, false);

    // 解決後のログインIDを診断出力に含めない
    public override string ToString() => $"LoginIdResolution(broken={IsBroken})";
}

/// <summary>Read-only lookups over a document. Pure functions.</summary>
public static class VaultQueries
{
    public const string BrokenReferenceText = "（参照エラー）";
    public const string MissingIdText = "（ID未設定）";

    public static ServiceEntry? FindService(VaultDocument document, Guid serviceId)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.Services.FirstOrDefault(s => s.Id == serviceId);
    }

    /// <summary>The service whose name matches <paramref name="name"/> ignoring case and surrounding spaces, or null (also for a blank name).</summary>
    public static ServiceEntry? FindServiceByName(VaultDocument document, string? name)
    {
        ArgumentNullException.ThrowIfNull(document);
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return document.Services.FirstOrDefault(s => string.Equals(s.Name?.Trim(), trimmed, StringComparison.OrdinalIgnoreCase));
    }

    public static AccountEntry? FindAccount(VaultDocument document, Guid accountId)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.Accounts.FirstOrDefault(a => a.Id == accountId);
    }

    public static IReadOnlyList<AccountEntry> AccountsOf(VaultDocument document, Guid serviceId)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.Accounts.Where(a => a.ServiceId == serviceId).ToList();
    }

    /// <summary>Accounts that may be chosen as the login ID of other accounts: email accounts with a directly entered address.</summary>
    public static IReadOnlyList<AccountEntry> EmailCandidates(VaultDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.Accounts
            .Where(a => a.IsEmailAccount && a.LoginIdMode == LoginIdMode.Direct && !string.IsNullOrWhiteSpace(a.LoginIdText))
            .ToList();
    }

    public static LoginIdResolution ResolveLoginId(VaultDocument document, AccountEntry account)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(account);
        if (account.LoginIdMode == LoginIdMode.Direct)
        {
            return LoginIdResolution.Resolved(account.LoginIdText ?? string.Empty);
        }

        if (account.LoginEmailAccountId is not { } targetId)
        {
            return LoginIdResolution.Broken;
        }

        var target = FindAccount(document, targetId);
        if (target is null || !target.IsEmailAccount || target.LoginIdMode != LoginIdMode.Direct || string.IsNullOrWhiteSpace(target.LoginIdText))
        {
            return LoginIdResolution.Broken;
        }

        return LoginIdResolution.Resolved(target.LoginIdText);
    }

    /// <summary>Accounts whose login ID references the given email account.</summary>
    public static IReadOnlyList<AccountEntry> ReferrersOf(VaultDocument document, Guid emailAccountId)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.Accounts
            .Where(a => a.LoginIdMode == LoginIdMode.EmailReference && a.LoginEmailAccountId == emailAccountId)
            .ToList();
    }

    /// <summary>The label, or the effective login ID when the label is empty.</summary>
    public static string DisplayName(VaultDocument document, AccountEntry account)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (!string.IsNullOrWhiteSpace(account.Label))
        {
            return account.Label;
        }

        var resolution = ResolveLoginId(document, account);
        if (resolution.IsBroken)
        {
            return BrokenReferenceText;
        }

        return string.IsNullOrEmpty(resolution.Value) ? MissingIdText : resolution.Value;
    }

    /// <summary>"address — Service／Label" used in the email candidate list.</summary>
    public static string EmailCandidateLabel(VaultDocument document, AccountEntry emailAccount)
    {
        ArgumentNullException.ThrowIfNull(emailAccount);
        var serviceName = FindService(document, emailAccount.ServiceId)?.Name ?? "（不明なサービス）";
        var address = emailAccount.LoginIdText ?? string.Empty;
        return string.IsNullOrWhiteSpace(emailAccount.Label)
            ? $"{address} — {serviceName}"
            : $"{address} — {serviceName}／{emailAccount.Label}";
    }
}
