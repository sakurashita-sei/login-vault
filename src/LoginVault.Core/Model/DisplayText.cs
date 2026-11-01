namespace LoginVault.Core.Model;

/// <summary>Japanese display strings for enum values (the UI language of the app is Japanese).</summary>
public static class DisplayText
{
    public static string Of(MfaStatus status) => status switch
    {
        MfaStatus.Unverified => "未設定",
        MfaStatus.NotConfigured => "未設定",
        MfaStatus.Enabled => "設定あり",
        _ => status.ToString(),
    };

    public static string Of(MfaMethodType type) => type switch
    {
        MfaMethodType.Sms => "SMS",
        MfaMethodType.Email => "メール",
        MfaMethodType.Authenticator => "Authenticator",
        MfaMethodType.Other => "その他",
        _ => type.ToString(),
    };

    /// <summary>Builds "種別（その他の方式名）：補足" for list display.</summary>
    public static string Summary(MfaMethod method)
    {
        ArgumentNullException.ThrowIfNull(method);
        var text = Of(method.Type);
        if (method.Type == MfaMethodType.Other && !string.IsNullOrWhiteSpace(method.CustomType))
        {
            text += $"（{method.CustomType}）";
        }

        if (!string.IsNullOrWhiteSpace(method.Detail))
        {
            text += $"：{method.Detail}";
        }

        return text;
    }
}
