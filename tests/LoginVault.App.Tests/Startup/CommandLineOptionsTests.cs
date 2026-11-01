using System.IO;
using LoginVault.App.Startup;

namespace LoginVault.App.Tests.Startup;

public class CommandLineOptionsTests
{
    [Fact]
    public void NoArguments_UsesDefaults()
    {
        var options = CommandLineOptions.Parse(Array.Empty<string>());

        Assert.True(options.IsValid);
        Assert.Null(options.DataDirectory);
    }

    [Theory]
    [InlineData("--data-dir", @"C:\work\dev-data")]
    [InlineData("--data-dir=C:\\work\\dev-data", null)]
    public void DataDir_IsParsed(string first, string? second)
    {
        var args = second is null ? new[] { first } : new[] { first, second };

        var options = CommandLineOptions.Parse(args);

        Assert.True(options.IsValid);
        Assert.Equal(@"C:\work\dev-data", options.DataDirectory);
    }

    [Fact]
    public void DataDir_WithoutValue_IsError()
    {
        var options = CommandLineOptions.Parse(new[] { "--data-dir" });

        Assert.False(options.IsValid);
        Assert.NotEmpty(options.Errors);
    }

    [Theory]
    [InlineData("--password")]
    [InlineData("--master-password=abc")]
    [InlineData("-p")]
    [InlineData("stray")]
    public void UnknownArguments_AreRejected(string arg)
    {
        var options = CommandLineOptions.Parse(new[] { arg });

        Assert.False(options.IsValid);
        Assert.Contains(options.Errors, e => e.Contains("不明", StringComparison.Ordinal));
    }

    [Fact]
    public void DefaultDataDirectory_IsLoginVaultUnderLocalAppData()
    {
        var resolved = DataDirectoryResolver.Resolve(null, @"C:\Users\someone\AppData\Local");

        Assert.Equal(@"C:\Users\someone\AppData\Local\LoginVault", resolved);
    }

    [Fact]
    public void ExplicitDataDirectory_IsUsedAsIs()
    {
        var resolved = DataDirectoryResolver.Resolve(@"D:\dev\data", @"C:\Users\someone\AppData\Local");

        Assert.Equal(@"D:\dev\data", resolved);
    }

    [Fact]
    public void SingleInstanceName_DependsOnDataDirectory_AndContainsNoPath()
    {
        var a = SingleInstanceGuard.MutexNameFor(@"C:\Users\someone\AppData\Local\LoginVault");
        var b = SingleInstanceGuard.MutexNameFor(@"C:\work\dev-data");
        var aAgain = SingleInstanceGuard.MutexNameFor(@"c:\users\SOMEONE\AppData\Local\LoginVault\");

        Assert.NotEqual(a, b);
        Assert.Equal(a, aAgain);
        Assert.StartsWith(@"Local\LoginVault.", a, StringComparison.Ordinal);
        Assert.DoesNotContain("someone", a, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SecondGuardForSameDirectory_IsNotPrimary_UntilFirstIsDisposed()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "testdata", "instance-" + Guid.NewGuid().ToString("N"));

        using (var first = new SingleInstanceGuard(directory))
        {
            Assert.True(first.IsPrimaryInstance);
            using var second = new SingleInstanceGuard(directory);
            Assert.False(second.IsPrimaryInstance);
            using var other = new SingleInstanceGuard(directory + "-other");
            Assert.True(other.IsPrimaryInstance);
        }

        using var third = new SingleInstanceGuard(directory);
        Assert.True(third.IsPrimaryInstance);
    }

    [Theory]
    [InlineData(new string[0], null)]
    [InlineData(new[] { "--theme", "dark" }, ThemeOverride.Dark)]
    [InlineData(new[] { "--theme=light" }, ThemeOverride.Light)]
    [InlineData(new[] { "--theme", "SYSTEM" }, ThemeOverride.System)]
    [InlineData(new[] { "--theme", "sf" }, ThemeOverride.Sf)]
    [InlineData(new[] { "--theme=SF" }, ThemeOverride.Sf)]
    public void Theme_IsParsed(string[] args, ThemeOverride? expected)
    {
        var options = CommandLineOptions.Parse(args);

        Assert.True(options.IsValid);
        Assert.Equal(expected, options.Theme);
    }

    [Theory]
    [InlineData("--theme")]
    [InlineData("--theme=blue")]
    public void Theme_InvalidValue_IsError(string arg)
    {
        var options = CommandLineOptions.Parse(new[] { arg });

        Assert.False(options.IsValid);
        Assert.NotEmpty(options.Errors);
    }

    [Fact]
    public void UnknownPositionalArgument_IsRejected_WithoutEchoingItsValue()
    {
        // オプション名でない引数は値そのものなので、誤って渡した秘密が画面に出ないよう一切表示しない
        var options = CommandLineOptions.Parse(new[] { "hunter2-架空の秘密" });

        Assert.False(options.IsValid);
        var error = Assert.Single(options.Errors);
        Assert.Contains("不明", error, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", error, StringComparison.Ordinal);
    }
}
