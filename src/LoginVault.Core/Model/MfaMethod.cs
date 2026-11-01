namespace LoginVault.Core.Model;

/// <summary>A recorded two-factor authentication method. Codes or seeds are never stored.</summary>
/// <param name="Type">The method kind.</param>
/// <param name="CustomType">Required free-text name when <paramref name="Type"/> is <see cref="MfaMethodType.Other"/>.</param>
/// <param name="Detail">Optional note such as the last digits of a phone number or the device name.</param>
public sealed record MfaMethod(MfaMethodType Type, string? CustomType = null, string? Detail = null)
{
    // 補足（Detail）が診断出力へ混ざらないよう、種別だけを返す
    public override string ToString() => $"MfaMethod({Type})";
}
