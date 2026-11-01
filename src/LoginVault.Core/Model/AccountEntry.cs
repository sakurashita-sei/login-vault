namespace LoginVault.Core.Model;

/// <summary>Login information for one account that belongs to a service.</summary>
public sealed record AccountEntry
{
    public required Guid Id { get; init; }

    public required Guid ServiceId { get; init; }

    /// <summary>Optional display name such as メイン or サブ. When empty the login ID is shown instead.</summary>
    public string Label { get; init; } = string.Empty;

    public required LoginIdMode LoginIdMode { get; init; }

    /// <summary>The login ID when <see cref="LoginIdMode"/> is <see cref="LoginIdMode.Direct"/>; otherwise null.</summary>
    public string? LoginIdText { get; init; }

    /// <summary>The referenced email account when <see cref="LoginIdMode"/> is <see cref="LoginIdMode.EmailReference"/>; otherwise null.</summary>
    public Guid? LoginEmailAccountId { get; init; }

    /// <summary>The password exactly as entered (whitespace, Unicode and symbols are preserved). Logical field only; never stored in plaintext.</summary>
    public string Password { get; init; } = string.Empty;

    /// <summary>True when this entry is itself the login of an email account, which makes it selectable as a login ID for other accounts.</summary>
    public bool IsEmailAccount { get; init; }

    public MfaStatus MfaStatus { get; init; } = MfaStatus.Unverified;

    public IReadOnlyList<MfaMethod> MfaMethods { get; init; } = Array.Empty<MfaMethod>();

    public string Notes { get; init; } = string.Empty;

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    // record の既定 ToString は全プロパティ（パスワード・メモ含む）を出力するため上書きする
    public override string ToString() => $"AccountEntry({Id})";
}
