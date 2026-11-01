namespace LoginVault.Core.Validation;

public enum IssueSeverity
{
    Warning = 0,
    Error = 1,
}

/// <summary>Machine-readable reason of a validation issue.</summary>
public enum IssueCode
{
    UnsupportedSchemaVersion,
    InvalidPreferences,
    DuplicateServiceId,
    DuplicateAccountId,
    ServiceNameRequired,
    DuplicateServiceName,
    ServiceNotFound,
    AccountNotFound,
    UnknownService,
    LoginIdRequired,
    LoginIdTextNotAllowedForReference,
    ReferenceRequired,
    ReferenceNotAllowedForDirect,
    ReferenceTargetMissing,
    ReferenceTargetNotEmailAccount,
    ReferenceTargetNotDirect,
    SelfReference,
    EmailAccountMustBeDirect,
    InvalidEmailFormat,
    MfaMethodsRequiredWhenEnabled,
    MfaMethodsMustBeEmptyWhenNotEnabled,
    OtherMfaMethodNeedsCustomType,
    DuplicateLoginIdInService,
    EmptyPassword,
    AccountIsReferenced,
    ServiceHasExternalReferences,
    NotAReference,

    /// <summary>A required collection, element or text is null (malformed document).</summary>
    MissingValue,

    /// <summary>An enum field holds a value this version does not define.</summary>
    InvalidValue,

    /// <summary>More services, accounts or MFA methods than <see cref="Model.VaultLimits"/> allows.</summary>
    TooManyItems,

    /// <summary>A text field is longer than <see cref="Model.VaultLimits"/> allows.</summary>
    ValueTooLong,

    /// <summary>The report was cut off at <see cref="Model.VaultLimits.MaxReportedIssues"/> issues.</summary>
    TooManyIssues,
}
