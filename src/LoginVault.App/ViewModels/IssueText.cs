using LoginVault.Core.Validation;

namespace LoginVault.App.ViewModels;

/// <summary>Formats validation issues for dialogs. Messages never include passwords.</summary>
internal static class IssueText
{
    public static string Describe(IEnumerable<ValidationIssue> issues) =>
        string.Join(Environment.NewLine, issues.Select(i => "・" + i.Message).Distinct());
}
