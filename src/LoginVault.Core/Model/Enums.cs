namespace LoginVault.Core.Model;

/// <summary>How an account's login ID is specified.</summary>
public enum LoginIdMode
{
    /// <summary>The login ID is typed directly and stored in <see cref="AccountEntry.LoginIdText"/>.</summary>
    Direct = 0,

    /// <summary>The login ID is the address of a registered email account (see <see cref="AccountEntry.LoginEmailAccountId"/>).</summary>
    EmailReference = 1,
}

/// <summary>Two-factor authentication status of an account.</summary>
public enum MfaStatus
{
    /// <summary>未確認: legacy unverified status, displayed as 未設定.</summary>
    Unverified = 0,

    /// <summary>未設定: confirmed that no second factor is configured.</summary>
    NotConfigured = 1,

    /// <summary>設定あり: one or more methods are configured.</summary>
    Enabled = 2,
}

/// <summary>Kind of a two-factor authentication method.</summary>
public enum MfaMethodType
{
    Sms = 0,
    Email = 1,
    Authenticator = 2,
    Other = 3,
}
