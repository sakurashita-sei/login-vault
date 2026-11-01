namespace LoginVault.Core.Validation;

/// <summary>
/// Basic syntactic check for email addresses used as login IDs of email accounts.
/// Deliberately conservative and not RFC 5322 complete; existence is never verified.
/// </summary>
public static class EmailAddressFormat
{
    private const int MaxLocalPartLength = 64;
    private const int MaxLabelLength = 63;

    public static bool IsValid(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        // 前後を含むあらゆる空白・制御文字を含む値は不正とする（trim はしない）
        foreach (var c in value)
        {
            if (char.IsWhiteSpace(c) || char.IsControl(c))
            {
                return false;
            }
        }

        var at = value.IndexOf('@', StringComparison.Ordinal);
        if (at <= 0 || at != value.LastIndexOf('@') || at == value.Length - 1)
        {
            return false;
        }

        var local = value[..at];
        var domain = value[(at + 1)..];
        if (local.Length > MaxLocalPartLength)
        {
            return false;
        }

        var labels = domain.Split('.');
        if (labels.Length < 2)
        {
            return false;
        }

        foreach (var label in labels)
        {
            if (label.Length == 0 || label.Length > MaxLabelLength || label[0] == '-' || label[^1] == '-')
            {
                return false;
            }
        }

        return true;
    }
}
