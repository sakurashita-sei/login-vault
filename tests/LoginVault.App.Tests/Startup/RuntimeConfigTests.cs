using System.IO;
using System.Text.Json;

namespace LoginVault.App.Tests.Startup;

/// <summary>
/// The app's runtimeconfig.json is copied next to the test assembly by the project reference. Two runtime
/// knobs must stay off: startup hooks (DOTNET_STARTUP_HOOKS could inject code into the process) and the
/// BinaryFormatter (WPF enables it by default; the app never uses it).
/// </summary>
public class RuntimeConfigTests
{
    [Fact]
    public void AppRuntimeConfig_DisablesStartupHooks_AndBinaryFormatter()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "LoginVault.runtimeconfig.json");
        Assert.True(File.Exists(path), path);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var properties = document.RootElement.GetProperty("runtimeOptions").GetProperty("configProperties");

        Assert.True(properties.TryGetProperty("System.StartupHookProvider.IsSupported", out var hooks), "StartupHookSupport が runtimeconfig に出力されていません。");
        Assert.False(hooks.GetBoolean());
        Assert.True(properties.TryGetProperty("System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization", out var formatter));
        Assert.False(formatter.GetBoolean());
    }
}
