using System.IO;
using System.Text.Json;
using LoginVault.App.Startup;
using LoginVault.App.Tests.Support;

namespace LoginVault.App.Tests.Startup;

/// <summary>
/// The theme choice is the only setting kept outside the encrypted vault (D-033): a tiny JSON file in the
/// data directory holding nothing but the theme name, so the lock screen and splash can use it.
/// </summary>
public class ThemeSettingsTests : IDisposable
{
    private readonly TempDataDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Load_WithoutFile_FollowsSystem()
    {
        Assert.Equal(ThemeOverride.System, ThemeSettings.Load(_dir.Path));
    }

    [Theory]
    [InlineData(ThemeOverride.System)]
    [InlineData(ThemeOverride.Light)]
    [InlineData(ThemeOverride.Dark)]
    [InlineData(ThemeOverride.Sf)]
    public void SaveThenLoad_RoundTrips(ThemeOverride theme)
    {
        ThemeSettings.Save(_dir.Path, theme);

        Assert.Equal(theme, ThemeSettings.Load(_dir.Path));
    }

    [Fact]
    public void Save_WritesOnlyTheThemeName()
    {
        ThemeSettings.Save(_dir.Path, ThemeOverride.Sf);

        var path = ThemeSettings.PathFor(_dir.Path);
        Assert.Equal(Path.Combine(_dir.Path, "ui-settings.json"), path);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var properties = document.RootElement.EnumerateObject().ToList();
        Assert.Single(properties);
        Assert.Equal("theme", properties[0].Name);
        Assert.Equal("sf", properties[0].Value.GetString());
        Assert.True(new FileInfo(path).Length < 64);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"theme\":\"blue\"}")]
    [InlineData("{\"theme\":42}")]
    [InlineData("{\"other\":\"sf\"}")]
    [InlineData("[]")]
    public void Load_InvalidOrUnknownContent_FollowsSystem(string content)
    {
        File.WriteAllText(ThemeSettings.PathFor(_dir.Path), content);

        Assert.Equal(ThemeOverride.System, ThemeSettings.Load(_dir.Path));
    }

    [Fact]
    public void Save_OverwritesPreviousChoice()
    {
        ThemeSettings.Save(_dir.Path, ThemeOverride.Dark);
        ThemeSettings.Save(_dir.Path, ThemeOverride.Light);

        Assert.Equal(ThemeOverride.Light, ThemeSettings.Load(_dir.Path));
        Assert.Empty(Directory.GetFiles(_dir.Path, "*.tmp"));
    }

    [Theory]
    [InlineData(null, ThemeOverride.Sf, ThemeOverride.Sf)]
    [InlineData(null, ThemeOverride.System, ThemeOverride.System)]
    [InlineData(ThemeOverride.System, ThemeOverride.Sf, ThemeOverride.System)]
    [InlineData(ThemeOverride.Dark, ThemeOverride.Sf, ThemeOverride.Dark)]
    [InlineData(ThemeOverride.Light, ThemeOverride.System, ThemeOverride.Light)]
    public void Resolve_AnExplicitCommandLineTheme_WinsForThatRun_OtherwiseTheSavedChoice(ThemeOverride? commandLine, ThemeOverride saved, ThemeOverride expected)
    {
        Assert.Equal(expected, ThemeSettings.Resolve(commandLine, saved));
    }

    [Fact]
    public void Load_OversizedFile_FollowsSystem()
    {
        // 同一ユーザーのプロセスが巨大なファイルに置き換えても、読み込みで起動が止まらない
        var json = "{\"theme\":\"sf\",\"padding\":\"" + new string('x', 64 * 1024) + "\"}";
        File.WriteAllText(ThemeSettings.PathFor(_dir.Path), json);

        Assert.Equal(ThemeOverride.System, ThemeSettings.Load(_dir.Path));
    }
}
