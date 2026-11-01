using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using LoginVault.App.Startup;
using Microsoft.Win32;

namespace LoginVault.App.Themes;

/// <summary>Palette to apply: light or dark (following the Windows "app mode" setting) or the SF look.</summary>
public enum FluentThemeMode
{
    Light,
    Dark,

    /// <summary>Science-fiction look: deep navy surfaces with cyan lines and glow; the accent is fixed.</summary>
    Sf,
}

/// <summary>
/// Applies the Fluent color dictionaries at runtime and keeps them in sync with the Windows theme and
/// accent color. Control styles reference the palette through DynamicResource, so swapping the
/// dictionary restyles every open window.
/// </summary>
public static class FluentTheme
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AppsUseLightThemeValue = "AppsUseLightTheme";
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private static FluentThemeMode _currentMode = FluentThemeMode.Light;
    private static bool _chromeHooked;

    /// <summary>Segoe Fluent Icons (Windows 11) with Segoe MDL2 Assets (Windows 10) as fallback.</summary>
    public static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    /// <summary>The mode most recently passed to <see cref="Apply"/>.</summary>
    public static FluentThemeMode CurrentMode => _currentMode;

    /// <summary>Maps the --theme override (or the in-app selection) to a mode; System follows the Windows app mode.</summary>
    public static FluentThemeMode ResolveMode(ThemeOverride overrideValue, FluentThemeMode systemMode) => overrideValue switch
    {
        ThemeOverride.Light => FluentThemeMode.Light,
        ThemeOverride.Dark => FluentThemeMode.Dark,
        ThemeOverride.Sf => FluentThemeMode.Sf,
        _ => systemMode,
    };

    /// <summary>Reads the Windows app mode. Defaults to light when the value is missing.</summary>
    public static FluentThemeMode DetectSystemMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return ParseAppsUseLightTheme(key?.GetValue(AppsUseLightThemeValue));
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            return FluentThemeMode.Light;
        }
    }

    /// <summary>Interprets the AppsUseLightTheme registry value (DWORD 1 = light, 0 = dark).</summary>
    public static FluentThemeMode ParseAppsUseLightTheme(object? value) =>
        value is int i && i == 0 ? FluentThemeMode.Dark : FluentThemeMode.Light;

    /// <summary>The Windows accent color, or null when it cannot be read.</summary>
    public static Color? DetectSystemAccent()
    {
        var color = SystemParameters.WindowGlassColor;
        return color.A == 0 ? null : Color.FromRgb(color.R, color.G, color.B);
    }

    /// <summary>The fixed accent of the SF theme (the reference design's cyan).</summary>
    public static readonly Color SfAccent = Color.FromRgb(0x5E, 0xEA, 0xFF);

    /// <summary>Derives the accent fill, hover and pressed colors for the given mode (Fluent lightens accent on dark; SF ignores the system accent).</summary>
    public static AccentPalette BuildAccentPalette(Color accent, FluentThemeMode mode)
    {
        if (mode == FluentThemeMode.Sf)
        {
            return new AccentPalette(SfAccent, Color.FromRgb(0xB8, 0xF6, 0xFF), Color.FromRgb(0x3E, 0xD2, 0xEA), SfAccent, Color.FromRgb(0x04, 0x12, 0x1A));
        }

        if (mode == FluentThemeMode.Dark)
        {
            var fill = Blend(accent, Colors.White, 0.35);
            return new AccentPalette(fill, Blend(fill, Colors.Black, 0.08), Blend(fill, Colors.Black, 0.16), Blend(fill, Colors.White, 0.15), Colors.Black);
        }

        return new AccentPalette(accent, Blend(accent, Colors.White, 0.10), Blend(accent, Colors.White, 0.20), Blend(accent, Colors.Black, 0.10), Colors.White);
    }

    /// <summary>Swaps the color dictionary of <paramref name="app"/>, overrides the accent brushes and updates window title bars.</summary>
    public static void Apply(Application app, FluentThemeMode mode, Color? accent)
    {
        ArgumentNullException.ThrowIfNull(app);
        _currentMode = mode;
        HookWindowChrome();
        foreach (Window window in app.Windows)
        {
            ApplyWindowChrome(window);
        }

        var file = mode switch
        {
            FluentThemeMode.Dark => "Fluent.Dark.xaml",
            FluentThemeMode.Sf => "Fluent.Sf.xaml",
            _ => "Fluent.Light.xaml",
        };
        var dictionaries = app.Resources.MergedDictionaries;
        var existing = dictionaries.FirstOrDefault(IsPaletteDictionary);
        var replacement = new ResourceDictionary { Source = ThemeUri(file) };
        if (existing is not null)
        {
            dictionaries[dictionaries.IndexOf(existing)] = replacement;
        }
        else
        {
            dictionaries.Insert(0, replacement);
        }

        // SF のアクセントは固定（Windows のアクセント色には追従しない）
        if (mode == FluentThemeMode.Sf)
        {
            accent = SfAccent;
        }

        if (accent is { } color)
        {
            var palette = BuildAccentPalette(color, mode);
            replacement["FluentAccentBrush"] = Freeze(new SolidColorBrush(palette.Fill));
            replacement["FluentAccentHoverBrush"] = Freeze(new SolidColorBrush(palette.Hover));
            replacement["FluentAccentPressedBrush"] = Freeze(new SolidColorBrush(palette.Pressed));
            replacement["FluentAccentTextBrush"] = Freeze(new SolidColorBrush(palette.Text));
            replacement["FluentTextOnAccentBrush"] = Freeze(new SolidColorBrush(palette.OnAccent));
        }
    }

    /// <summary>Asks DWM for a dark (or light) title bar so the window frame matches the theme (attribute 20: Windows 10 20H1 or later / Windows 11).</summary>
    public static void ApplyWindowChrome(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var useDark = _currentMode == FluentThemeMode.Light ? 0 : 1;
        // 失敗（古い Windows など）は無視し、既定のタイトルバーのままにする
        _ = NativeMethods.DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref useDark, sizeof(int));
        // 表示中のウィンドウは属性を変えてもタイトルバーが描き直されないことがあるため、フレームの再計算と再描画を要求する
        _ = NativeMethods.SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, SwpNoSize | SwpNoMove | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
    }

    private static void HookWindowChrome()
    {
        if (_chromeHooked)
        {
            return;
        }

        _chromeHooked = true;
        // 以後に作られるすべてのウィンドウ（ダイアログ含む）に、ハンドル生成後にタイトルバーの配色を適用する
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, _) =>
        {
            if (sender is Window window)
            {
                ApplyWindowChrome(window);
            }
        }));
    }

    /// <summary>Absolute pack URI of a theme dictionary, so lookup does not depend on the entry assembly (test hosts differ).</summary>
    private static Uri ThemeUri(string file) =>
        new($"pack://application:,,,/{typeof(FluentTheme).Assembly.GetName().Name};component/Themes/{file}", UriKind.Absolute);

    private static bool IsPaletteDictionary(ResourceDictionary dictionary) =>
        dictionary.Source is { } s
        && (s.OriginalString.EndsWith("Themes/Fluent.Light.xaml", StringComparison.OrdinalIgnoreCase)
            || s.OriginalString.EndsWith("Themes/Fluent.Dark.xaml", StringComparison.OrdinalIgnoreCase)
            || s.OriginalString.EndsWith("Themes/Fluent.Sf.xaml", StringComparison.OrdinalIgnoreCase));

    private static SolidColorBrush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }

    private static Color Blend(Color from, Color to, double amount)
    {
        byte Mix(byte a, byte b) => (byte)Math.Round(a + (b - a) * amount);
        return Color.FromRgb(Mix(from.R, to.R), Mix(from.G, to.G), Mix(from.B, to.B));
    }
}

/// <summary>Accent fill states plus the text colors used with them.</summary>
public sealed record AccentPalette(Color Fill, Color Hover, Color Pressed, Color Text, Color OnAccent);

// DLL の検索パスはアセンブリ属性（AssemblyInfo.cs）でシステムディレクトリに限定している
internal static class NativeMethods
{
    [DllImport("dwmapi.dll", PreserveSig = true)]
    internal static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr hwndInsertAfter, int x, int y, int cx, int cy, uint flags);
}
