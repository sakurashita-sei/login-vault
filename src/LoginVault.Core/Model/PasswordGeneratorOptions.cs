using System.Text.Json.Serialization;
using LoginVault.Core.Operations;

namespace LoginVault.Core.Model;

/// <summary>
/// Options of the password generator. Stored inside the encrypted vault (<see cref="VaultPreferences.PasswordGenerator"/>)
/// so the next generation starts from the last used settings. Documents written before this type existed read as the defaults.
/// </summary>
public sealed record PasswordGeneratorOptions
{
    public const int MinLength = 4;
    public const int MaxLength = 128;
    public const int DefaultLength = 12;

    /// <summary>Every symbol the generator can use, in display order (19 symbols).</summary>
    public const string AllSymbols = "-_/*+.,!#$%&()~|@^=";

    /// <summary>Characters that look alike; dropped from letters and digits when <see cref="ExcludeAmbiguous"/> is on.</summary>
    public const string AmbiguousCharacters = "Il1Oo0";

    /// <summary><see cref="NoRepeatedCharacters"/> applies to passwords up to this length.</summary>
    public const int NoRepeatMaxLength = 8;

    public int Length { get; init; } = DefaultLength;

    public bool UseUppercase { get; init; } = true;

    public bool UseLowercase { get; init; } = true;

    public bool UseDigits { get; init; } = true;

    public bool UseSymbols { get; init; } = true;

    /// <summary>The symbols used while <see cref="UseSymbols"/> is on: a subset of <see cref="AllSymbols"/> without duplicates. Kept when symbols are turned off.</summary>
    public string EnabledSymbols { get; init; } = AllSymbols;

    /// <summary>Leave out I, l, 1, O, o and 0.</summary>
    public bool ExcludeAmbiguous { get; init; } = true;

    /// <summary>For passwords of up to <see cref="NoRepeatMaxLength"/> characters, never use the same character twice.</summary>
    public bool NoRepeatedCharacters { get; init; } = true;

    [JsonIgnore]
    public bool IsValid => Validate() is null;

    /// <summary>Returns the user-facing reason why these options cannot produce a password, or null when they can.</summary>
    public string? Validate()
    {
        if (Length < MinLength || Length > MaxLength)
        {
            return $"文字数は {MinLength}〜{MaxLength} の範囲で指定してください。";
        }

        if (!UseUppercase && !UseLowercase && !UseDigits && !UseSymbols)
        {
            return "使う文字の種類を 1 つ以上選んでください。";
        }

        if (EnabledSymbols is null || EnabledSymbols.Any(c => !AllSymbols.Contains(c)) || EnabledSymbols.Distinct().Count() != EnabledSymbols.Length)
        {
            return "記号の設定が不正です。";
        }

        if (UseSymbols && EnabledSymbols.Length == 0)
        {
            return "記号を使う場合は、記号を 1 つ以上選んでください。";
        }

        if (NoRepeatedCharacters && Length <= NoRepeatMaxLength && PasswordGenerator.PoolSize(this) < Length)
        {
            return $"同じ文字を 2 回以上使わない設定では、選んだ文字の種類で {Length} 文字を作れません。文字の種類を増やすか、文字数を減らしてください。";
        }

        return null;
    }
}
