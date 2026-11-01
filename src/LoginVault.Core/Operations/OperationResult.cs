using LoginVault.Core.Model;
using LoginVault.Core.Validation;

namespace LoginVault.Core.Operations;

/// <summary>
/// Result of applying an operation to a document. On failure <see cref="Document"/> is the
/// unchanged original, so callers never persist a partially applied state.
/// </summary>
public sealed class OperationResult
{
    private OperationResult(bool succeeded, VaultDocument document, IReadOnlyList<ValidationIssue> issues, Guid? createdId)
    {
        Succeeded = succeeded;
        Document = document;
        Issues = issues;
        CreatedId = createdId;
    }

    public bool Succeeded { get; }

    public VaultDocument Document { get; }

    public IReadOnlyList<ValidationIssue> Issues { get; }

    public IReadOnlyList<ValidationIssue> Errors => Issues.Where(i => i.Severity == IssueSeverity.Error).ToList();

    public IReadOnlyList<ValidationIssue> Warnings => Issues.Where(i => i.Severity == IssueSeverity.Warning).ToList();

    /// <summary>The ID of a newly created service or account, when applicable.</summary>
    public Guid? CreatedId { get; }

    public static OperationResult Success(VaultDocument document, IReadOnlyList<ValidationIssue> warnings, Guid? createdId = null) =>
        new(true, document, warnings, createdId);

    public static OperationResult Failure(VaultDocument original, IReadOnlyList<ValidationIssue> issues) =>
        new(false, original, issues, null);

    public static OperationResult Failure(VaultDocument original, ValidationIssue issue) =>
        new(false, original, new[] { issue }, null);
}
