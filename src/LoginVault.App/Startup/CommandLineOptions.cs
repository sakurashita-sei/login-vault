using System.IO;

namespace LoginVault.App.Startup;

/// <summary>Forces the color theme instead of following the Windows app mode (for development and screenshots).</summary>
public enum ThemeOverride
{
    System,
    Light,
    Dark,
    Sf,
}

/// <summary>
/// Command line of LoginVault.exe. <c>--data-dir</c> points the app at a fictitious data directory for
/// development and tests; <c>--theme</c> forces a theme for that run (system, light, dark or sf; the saved
/// choice applies when omitted). Passwords are never accepted on the command line.
/// </summary>
public sealed class CommandLineOptions
{
    private const string DataDirOption = "--data-dir";
    private const string ThemeOption = "--theme";

    private CommandLineOptions(string? dataDirectory, ThemeOverride? theme, IReadOnlyList<string> errors)
    {
        DataDirectory = dataDirectory;
        Theme = theme;
        Errors = errors;
    }

    public string? DataDirectory { get; }

    /// <summary>The theme given on the command line, or null when --theme was not specified (the saved choice applies).</summary>
    public ThemeOverride? Theme { get; }

    public IReadOnlyList<string> Errors { get; }

    public bool IsValid => Errors.Count == 0;

    public static CommandLineOptions Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        string? dataDirectory = null;
        ThemeOverride? theme = null;
        var errors = new List<string>();
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (string.Equals(arg, ThemeOption, StringComparison.Ordinal) || arg.StartsWith(ThemeOption + "=", StringComparison.Ordinal))
            {
                string? value = null;
                if (arg.Length > ThemeOption.Length)
                {
                    value = arg[(ThemeOption.Length + 1)..];
                }
                else if (i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    value = args[++i];
                }

                if (TryParseTheme(value, out var parsed))
                {
                    theme = parsed;
                }
                else
                {
                    errors.Add($"{ThemeOption} には system、light、dark、sf のいずれかを指定してください。");
                }
            }
            else if (string.Equals(arg, DataDirOption, StringComparison.Ordinal))
            {
                if (i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    dataDirectory = args[++i];
                }
                else
                {
                    errors.Add($"{DataDirOption} にはデータディレクトリのパスを指定してください。");
                }
            }
            else if (arg.StartsWith(DataDirOption + "=", StringComparison.Ordinal))
            {
                dataDirectory = arg[(DataDirOption.Length + 1)..];
            }
            else if (!arg.StartsWith("-", StringComparison.Ordinal))
            {
                // オプション名でない引数は値そのもの（誤って渡した秘密かもしれない）なので一切表示しない
                errors.Add("不明な引数です（オプション名ではない値が渡されました。値は表示しません）。");
            }
            else
            {
                // 値を画面に出さない（誤って渡した秘密が表示されないよう、名前の部分だけ示す）
                var name = arg.Split('=', 2)[0];
                var shown = name.Length > 24 ? name[..24] + "…" : name;
                errors.Add($"不明な引数です: {shown}");
            }
        }

        if (dataDirectory is not null && string.IsNullOrWhiteSpace(dataDirectory))
        {
            errors.Add($"{DataDirOption} のパスが空です。");
            dataDirectory = null;
        }

        return new CommandLineOptions(dataDirectory, theme, errors);
    }

    /// <summary>Parses a theme name (system / light / dark / sf, case-insensitive). Numbers are not accepted.</summary>
    public static bool TryParseTheme(string? value, out ThemeOverride theme)
    {
        foreach (var candidate in Enum.GetValues<ThemeOverride>())
        {
            if (string.Equals(value, ThemeName(candidate), StringComparison.OrdinalIgnoreCase))
            {
                theme = candidate;
                return true;
            }
        }

        theme = ThemeOverride.System;
        return false;
    }

    /// <summary>The lowercase name used on the command line and in the settings file.</summary>
    public static string ThemeName(ThemeOverride theme) => theme.ToString().ToLowerInvariant();
}

/// <summary>Resolves the data directory: an explicit override, or %LOCALAPPDATA%\LoginVault.</summary>
public static class DataDirectoryResolver
{
    public const string DefaultFolderName = "LoginVault";

    public static string Resolve(string? overridePath, string localAppDataPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localAppDataPath);
        return string.IsNullOrWhiteSpace(overridePath) ? Path.Combine(localAppDataPath, DefaultFolderName) : overridePath;
    }

    public static string ResolveFromEnvironment(string? overridePath) =>
        Resolve(overridePath, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

}
