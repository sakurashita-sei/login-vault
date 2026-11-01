using System.Reflection;
using System.Text.RegularExpressions;
using LoginVault.App.Startup;

namespace LoginVault.App.Tests.Startup;

public class AppVersionTests
{
    [Fact]
    public void Product_IsTheThreePartVersion_WithoutBuildMetadata()
    {
        var product = AppVersion.Product;

        Assert.Matches(new Regex(@"^\d+\.\d+\.\d+$"), product);
        var informational = typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        Assert.StartsWith(product, informational, StringComparison.Ordinal);
    }
}
