using System.Windows.Media;
using LoginVault.App.Startup;
using LoginVault.App.Themes;

namespace LoginVault.App.Tests.Views;

public class FluentThemeTests
{
    [Theory]
    [InlineData(null, FluentThemeMode.Light)]
    [InlineData(1, FluentThemeMode.Light)]
    [InlineData(0, FluentThemeMode.Dark)]
    [InlineData("1", FluentThemeMode.Light)]
    public void ParseAppsUseLightTheme_MapsRegistryValue(object? value, FluentThemeMode expected)
    {
        Assert.Equal(expected, FluentTheme.ParseAppsUseLightTheme(value));
    }

    [Fact]
    public void LightPalette_UsesAccentAsFill_WithWhiteText_AndLighterHoverStates()
    {
        var accent = Color.FromRgb(0x00, 0x67, 0xC0);

        var palette = FluentTheme.BuildAccentPalette(accent, FluentThemeMode.Light);

        Assert.Equal(accent, palette.Fill);
        Assert.Equal(Colors.White, palette.OnAccent);
        Assert.True(Luminance(palette.Hover) > Luminance(palette.Fill));
        Assert.True(Luminance(palette.Pressed) > Luminance(palette.Hover));
    }

    [Fact]
    public void DarkPalette_LightensAccent_AndUsesBlackText()
    {
        var accent = Color.FromRgb(0x00, 0x67, 0xC0);

        var palette = FluentTheme.BuildAccentPalette(accent, FluentThemeMode.Dark);

        Assert.True(Luminance(palette.Fill) > Luminance(accent));
        Assert.Equal(Colors.Black, palette.OnAccent);
        Assert.True(Luminance(palette.Hover) < Luminance(palette.Fill));
    }

    [Fact]
    public void SfPalette_UsesFixedCyan_AndDarkTextOnAccent_RegardlessOfSystemAccent()
    {
        var systemAccent = Color.FromRgb(0xC4, 0x2B, 0x1C);

        var palette = FluentTheme.BuildAccentPalette(systemAccent, FluentThemeMode.Sf);

        Assert.Equal(FluentTheme.SfAccent, palette.Fill);
        Assert.Equal(FluentTheme.SfAccent, palette.Text);
        Assert.True(Luminance(palette.OnAccent) < 60, "アクセント上の文字は暗い色でなければなりません。");
        Assert.True(Luminance(palette.Hover) > Luminance(palette.Fill));
        Assert.True(Luminance(palette.Pressed) < Luminance(palette.Fill));
    }

    [Theory]
    [InlineData(ThemeOverride.System, FluentThemeMode.Dark, FluentThemeMode.Dark)]
    [InlineData(ThemeOverride.System, FluentThemeMode.Light, FluentThemeMode.Light)]
    [InlineData(ThemeOverride.Light, FluentThemeMode.Dark, FluentThemeMode.Light)]
    [InlineData(ThemeOverride.Dark, FluentThemeMode.Light, FluentThemeMode.Dark)]
    [InlineData(ThemeOverride.Sf, FluentThemeMode.Light, FluentThemeMode.Sf)]
    public void ResolveMode_UsesOverride_OrFallsBackToSystemMode(ThemeOverride overrideValue, FluentThemeMode systemMode, FluentThemeMode expected)
    {
        Assert.Equal(expected, FluentTheme.ResolveMode(overrideValue, systemMode));
    }

    private static double Luminance(Color c) => (0.299 * c.R) + (0.587 * c.G) + (0.114 * c.B);
}
