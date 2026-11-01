using System.Reflection;

namespace LoginVault.App.Startup;

/// <summary>The product version shown in the title bar and on the splash: Directory.Build.props Version without build metadata.</summary>
public static class AppVersion
{
    public static string Product { get; } = Read();

    private static string Read()
    {
        var assembly = typeof(AppVersion).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var version = string.IsNullOrEmpty(informational) ? assembly.GetName().Version?.ToString(3) : informational;
        // "0.1.0+<commit>" の "+" 以降（ビルドメタデータ）は表示しない
        var plus = version?.IndexOf('+', StringComparison.Ordinal) ?? -1;
        return plus >= 0 ? version![..plus] : version ?? "0.0.0";
    }
}
