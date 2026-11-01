namespace LoginVault.Core.Model;

/// <summary>
/// Size limits of a vault document. They sit far above personal use and exist so that a crafted backup
/// (restored with its own password) cannot exhaust time or memory in validation or in the UI.
/// </summary>
public static class VaultLimits
{
    public const int MaxServices = 5_000;

    public const int MaxAccounts = 20_000;

    /// <summary>Two-factor methods per account.</summary>
    public const int MaxMfaMethods = 16;

    /// <summary>Service name, account label, login ID, MFA custom type and MFA detail (characters).</summary>
    public const int MaxTextLength = 1_000;

    public const int MaxPasswordLength = 4_096;

    public const int MaxNotesLength = 20_000;

    /// <summary>Issues kept per validation; beyond this a single <see cref="Validation.IssueCode.TooManyIssues"/> marker is added.</summary>
    public const int MaxReportedIssues = 200;
}
