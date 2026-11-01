using LoginVault.Core.Model;
using LoginVault.Core.Operations;

namespace LoginVault.Core.Tests.Core;

public class PasswordGeneratorTests
{
    private const string Upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string Lower = "abcdefghijklmnopqrstuvwxyz";
    private const string Digits = "0123456789";

    /// <summary>A random source that always picks the first candidate: the worst case for "every class at least once".</summary>
    private static int AlwaysFirst(int exclusiveUpperBound)
    {
        Assert.True(exclusiveUpperBound > 0);
        return 0;
    }

    [Fact]
    public void Defaults_AreTwelveCharacters_AllClasses_AllSymbols_AndValid()
    {
        var options = new PasswordGeneratorOptions();

        Assert.Equal(12, options.Length);
        Assert.True(options.UseUppercase && options.UseLowercase && options.UseDigits && options.UseSymbols);
        Assert.Equal("-_/*+.,!#$%&()~|@^=", options.EnabledSymbols);
        Assert.Equal(19, PasswordGeneratorOptions.AllSymbols.Length);
        Assert.True(options.ExcludeAmbiguous);
        Assert.True(options.NoRepeatedCharacters);
        Assert.Null(options.Validate());
        Assert.True(options.IsValid);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(16)]
    [InlineData(128)]
    public void Generate_ProducesExactlyTheRequestedLength(int length)
    {
        var password = PasswordGenerator.Generate(new PasswordGeneratorOptions { Length = length });

        Assert.Equal(length, password.Length);
    }

    [Theory]
    [InlineData(true, false, false, false, Upper)]
    [InlineData(false, true, false, false, Lower)]
    [InlineData(false, false, true, false, Digits)]
    [InlineData(false, false, false, true, "-_/*+.,!#$%&()~|@^=")]
    public void Generate_UsesOnlyTheSelectedClasses(bool upper, bool lower, bool digits, bool symbols, string allowed)
    {
        var options = new PasswordGeneratorOptions { Length = 32, UseUppercase = upper, UseLowercase = lower, UseDigits = digits, UseSymbols = symbols };

        for (var i = 0; i < 50; i++)
        {
            var password = PasswordGenerator.Generate(options);
            Assert.All(password, c => Assert.Contains(c, allowed));
        }
    }

    [Theory]
    [InlineData(4)]
    [InlineData(12)]
    public void Generate_IncludesEveryEnabledClassAtLeastOnce_EvenWithABiasedRandomSource(int length)
    {
        var options = new PasswordGeneratorOptions { Length = length };

        var password = PasswordGenerator.Generate(options, AlwaysFirst);

        Assert.Contains(password, c => Upper.Contains(c));
        Assert.Contains(password, c => Lower.Contains(c));
        Assert.Contains(password, c => Digits.Contains(c));
        Assert.Contains(password, c => PasswordGeneratorOptions.AllSymbols.Contains(c));
    }

    [Fact]
    public void Generate_ExcludesAmbiguousCharacters_WhenRequested()
    {
        var options = new PasswordGeneratorOptions { Length = 64, UseSymbols = false, ExcludeAmbiguous = true };

        for (var i = 0; i < 100; i++)
        {
            var password = PasswordGenerator.Generate(options);
            Assert.DoesNotContain(password, c => PasswordGeneratorOptions.AmbiguousCharacters.Contains(c));
        }
    }

    [Fact]
    public void Generate_WithNoRepeats_UsesDistinctCharacters_UpToEight()
    {
        // 数字だけ・似通った文字を省く → 候補は 8 文字（2〜9）。8 文字を重複なしで作ると候補を使い切る
        var options = new PasswordGeneratorOptions { Length = 8, UseUppercase = false, UseLowercase = false, UseSymbols = false, ExcludeAmbiguous = true, NoRepeatedCharacters = true };

        for (var i = 0; i < 30; i++)
        {
            var password = PasswordGenerator.Generate(options);
            Assert.Equal(8, password.Distinct().Count());
            Assert.All(password, c => Assert.Contains(c, "23456789"));
        }

        // 9 文字以上では重複なしの条件は働かない（候補 8 文字でも作れる）
        var nine = PasswordGenerator.Generate(options with { Length = 9 });
        Assert.Equal(9, nine.Length);
    }

    [Fact]
    public void Generate_UsesOnlyTheEnabledSymbols()
    {
        var options = new PasswordGeneratorOptions { Length = 32, UseUppercase = false, UseLowercase = false, UseDigits = false, EnabledSymbols = "-_" };

        for (var i = 0; i < 30; i++)
        {
            Assert.All(PasswordGenerator.Generate(options), c => Assert.Contains(c, "-_"));
        }
    }

    [Fact]
    public void Generate_TwoCalls_DifferByDefault()
    {
        var options = new PasswordGeneratorOptions { Length = 16 };

        Assert.NotEqual(PasswordGenerator.Generate(options), PasswordGenerator.Generate(options));
    }

    public static IEnumerable<object[]> ImpossibleOptions()
    {
        yield return new object[] { new PasswordGeneratorOptions { UseUppercase = false, UseLowercase = false, UseDigits = false, UseSymbols = false }, "文字の種類" };
        yield return new object[] { new PasswordGeneratorOptions { EnabledSymbols = "" }, "記号" };
        yield return new object[] { new PasswordGeneratorOptions { Length = 3 }, "文字数" };
        yield return new object[] { new PasswordGeneratorOptions { Length = 129 }, "文字数" };
        yield return new object[] { new PasswordGeneratorOptions { Length = 8, UseUppercase = false, UseLowercase = false, UseDigits = false, EnabledSymbols = "-_/", NoRepeatedCharacters = true }, "同じ文字" };
        yield return new object[] { new PasswordGeneratorOptions { EnabledSymbols = "ab" }, "記号" };
        yield return new object[] { new PasswordGeneratorOptions { EnabledSymbols = "--" }, "記号" };
    }

    [Theory]
    [MemberData(nameof(ImpossibleOptions))]
    public void Validate_ReportsOptionsThatCannotProduceAPassword(PasswordGeneratorOptions options, string expectedWord)
    {
        var error = options.Validate();

        Assert.NotNull(error);
        Assert.Contains(expectedWord, error, StringComparison.Ordinal);
        Assert.False(options.IsValid);
        Assert.Throws<ArgumentException>(() => PasswordGenerator.Generate(options));
    }

    [Fact]
    public void NoRepeats_IsAcceptedWhenThePoolIsExactlyLargeEnough()
    {
        var options = new PasswordGeneratorOptions { Length = 8, UseUppercase = false, UseLowercase = false, UseSymbols = false, ExcludeAmbiguous = true, NoRepeatedCharacters = true };

        Assert.Null(options.Validate());
    }
}
