using LoginVault.Core.Model;

namespace LoginVault.Core.Operations;

/// <summary>Values entered in the account editor, independent of the stored entry.</summary>
public sealed record AccountInput
{
    public required Guid ServiceId { get; init; }

    public string Label { get; init; } = string.Empty;

    public LoginIdMode LoginIdMode { get; init; } = LoginIdMode.Direct;

    public string? LoginIdText { get; init; }

    public Guid? LoginEmailAccountId { get; init; }

    public required PasswordChange Password { get; init; }

    public bool IsEmailAccount { get; init; }

    public MfaStatus MfaStatus { get; init; } = MfaStatus.Unverified;

    public IReadOnlyList<MfaMethod> MfaMethods { get; init; } = Array.Empty<MfaMethod>();

    public string Notes { get; init; } = string.Empty;

    /// <summary>Creates an input that reproduces an existing account with the password kept.</summary>
    public static AccountInput FromAccount(AccountEntry account)
    {
        ArgumentNullException.ThrowIfNull(account);
        return new AccountInput
        {
            ServiceId = account.ServiceId,
            Label = account.Label,
            LoginIdMode = account.LoginIdMode,
            LoginIdText = account.LoginIdText,
            LoginEmailAccountId = account.LoginEmailAccountId,
            Password = PasswordChange.Keep,
            IsEmailAccount = account.IsEmailAccount,
            MfaStatus = account.MfaStatus,
            MfaMethods = account.MfaMethods,
            Notes = account.Notes,
        };
    }

    public override string ToString() => $"AccountInput(service={ServiceId})";
}
