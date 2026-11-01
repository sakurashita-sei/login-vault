namespace LoginVault.Core.Validation;

/// <summary>One validation finding. Messages never contain passwords or login IDs.</summary>
public sealed record ValidationIssue(
    IssueSeverity Severity,
    IssueCode Code,
    string Message,
    Guid? ServiceId = null,
    Guid? AccountId = null)
{
    public static ValidationIssue Error(IssueCode code, string message, Guid? serviceId = null, Guid? accountId = null) =>
        new(IssueSeverity.Error, code, message, serviceId, accountId);

    public static ValidationIssue Warning(IssueCode code, string message, Guid? serviceId = null, Guid? accountId = null) =>
        new(IssueSeverity.Warning, code, message, serviceId, accountId);
}

/// <summary>The outcome of validating a whole document.</summary>
public sealed class ValidationReport
{
    public static ValidationReport Empty { get; } = new(Array.Empty<ValidationIssue>());

    public ValidationReport(IReadOnlyList<ValidationIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        Issues = issues;
        Errors = issues.Where(i => i.Severity == IssueSeverity.Error).ToList();
        Warnings = issues.Where(i => i.Severity == IssueSeverity.Warning).ToList();
    }

    public IReadOnlyList<ValidationIssue> Issues { get; }

    public IReadOnlyList<ValidationIssue> Errors { get; }

    public IReadOnlyList<ValidationIssue> Warnings { get; }

    public bool HasErrors => Errors.Count > 0;
}
