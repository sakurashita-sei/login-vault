using System.IO;
using System.Text.Json;

namespace LoginVault.App.Startup;

/// <summary>
/// Persists the theme choice as the only setting kept outside the encrypted vault (D-033): the lock
/// screen and the splash need it before the vault is unlocked. The file holds nothing but the theme name.
/// </summary>
public static class ThemeSettings
{
    public const string FileName = "ui-settings.json";

    /// <summary>Anything larger than this was not written by this app and is ignored without being read.</summary>
    public const long MaxFileLength = 4096;

    private const string ThemeProperty = "theme";

    public static string PathFor(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        return Path.Combine(Path.GetFullPath(dataDirectory), FileName);
    }

    /// <summary>Reads the saved theme; a missing, unreadable or unknown value means "follow Windows".</summary>
    public static ThemeOverride Load(string dataDirectory)
    {
        var path = PathFor(dataDirectory);
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > MaxFileLength)
            {
                return ThemeOverride.System;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty(ThemeProperty, out var value)
                || value.ValueKind != JsonValueKind.String)
            {
                return ThemeOverride.System;
            }

            return CommandLineOptions.TryParseTheme(value.GetString(), out var theme) ? theme : ThemeOverride.System;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // 設定ファイルの破損や読み取り失敗は既定（Windows の設定に追従）に倒す
            return ThemeOverride.System;
        }
    }

    /// <summary>Writes the theme name atomically (temporary file, then replace). Throws on I/O failure.</summary>
    public static void Save(string dataDirectory, ThemeOverride theme)
    {
        var path = PathFor(dataDirectory);
        Directory.CreateDirectory(dataDirectory);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var json = JsonSerializer.Serialize(new Dictionary<string, string> { [ThemeProperty] = CommandLineOptions.ThemeName(theme) });
        try
        {
            File.WriteAllText(temporary, json);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    /// <summary>An explicit --theme (including system) wins for that run; otherwise the saved choice applies.</summary>
    public static ThemeOverride Resolve(ThemeOverride? commandLine, ThemeOverride saved) => commandLine ?? saved;
}
