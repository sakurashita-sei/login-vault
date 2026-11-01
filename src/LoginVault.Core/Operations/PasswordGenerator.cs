using System.Security.Cryptography;
using LoginVault.Core.Model;

namespace LoginVault.Core.Operations;

/// <summary>
/// Generates passwords from <see cref="PasswordGeneratorOptions"/> with a cryptographic random source and
/// uniform selection. Every enabled character class appears at least once; the result is shuffled so the
/// guaranteed characters do not sit at fixed positions. Nothing is logged or kept.
/// </summary>
public static class PasswordGenerator
{
    public const string Uppercase = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    public const string Lowercase = "abcdefghijklmnopqrstuvwxyz";
    public const string Digits = "0123456789";

    /// <summary>
    /// Generates one password. <paramref name="nextIndex"/> maps an exclusive upper bound to a uniform index in
    /// [0, bound); it defaults to <see cref="RandomNumberGenerator.GetInt32(int)"/> and exists for deterministic tests.
    /// </summary>
    public static string Generate(PasswordGeneratorOptions options, Func<int, int>? nextIndex = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        var error = options.Validate();
        if (error is not null)
        {
            throw new ArgumentException(error, nameof(options));
        }

        var random = nextIndex ?? RandomNumberGenerator.GetInt32;
        var classes = CharacterClasses(options);
        var pool = classes.SelectMany(c => c).ToArray();
        var noRepeat = options.NoRepeatedCharacters && options.Length <= PasswordGeneratorOptions.NoRepeatMaxLength;
        var chosen = new List<char>(options.Length);

        // 選んだ文字種をそれぞれ 1 文字ずつ必ず含める（MinLength=4 ≧ 文字種の数なので常に収まる）
        foreach (var characterClass in classes)
        {
            chosen.Add(Pick(characterClass, chosen, noRepeat, random));
        }

        while (chosen.Count < options.Length)
        {
            chosen.Add(Pick(pool, chosen, noRepeat, random));
        }

        Shuffle(chosen, random);
        return new string(chosen.ToArray());
    }

    /// <summary>Number of distinct characters the options can draw from (used to check "no repeated characters").</summary>
    public static int PoolSize(PasswordGeneratorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return CharacterClasses(options).Sum(c => c.Length);
    }

    private static IReadOnlyList<char[]> CharacterClasses(PasswordGeneratorOptions options)
    {
        var classes = new List<char[]>();
        if (options.UseUppercase)
        {
            classes.Add(WithoutAmbiguous(Uppercase, options.ExcludeAmbiguous));
        }

        if (options.UseLowercase)
        {
            classes.Add(WithoutAmbiguous(Lowercase, options.ExcludeAmbiguous));
        }

        if (options.UseDigits)
        {
            classes.Add(WithoutAmbiguous(Digits, options.ExcludeAmbiguous));
        }

        if (options.UseSymbols)
        {
            classes.Add((options.EnabledSymbols ?? string.Empty).ToCharArray());
        }

        return classes;
    }

    private static char[] WithoutAmbiguous(string characters, bool exclude) =>
        exclude ? characters.Where(c => !PasswordGeneratorOptions.AmbiguousCharacters.Contains(c)).ToArray() : characters.ToCharArray();

    private static char Pick(char[] source, List<char> chosen, bool noRepeat, Func<int, int> random)
    {
        var candidates = noRepeat ? source.Where(c => !chosen.Contains(c)).ToArray() : source;
        return candidates[random(candidates.Length)];
    }

    /// <summary>Fisher–Yates shuffle with the same random source.</summary>
    private static void Shuffle(List<char> items, Func<int, int> random)
    {
        for (var i = items.Count - 1; i > 0; i--)
        {
            var j = random(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }
}
