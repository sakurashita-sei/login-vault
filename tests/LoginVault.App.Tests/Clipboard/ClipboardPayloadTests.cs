using System.Text;
using LoginVault.App.Clipboard;

namespace LoginVault.App.Tests.Clipboard;

public class ClipboardPayloadTests
{
    [Fact]
    public void Payload_UsesDocumentedExclusionFormatNames()
    {
        var payload = ClipboardPayload.ForSecret("secret-架空", Guid.NewGuid());
        var names = payload.RegisteredFormats.Select(f => f.FormatName).ToList();

        Assert.Contains("ExcludeClipboardContentFromMonitorProcessing", names);
        Assert.Contains("CanIncludeInClipboardHistory", names);
        Assert.Contains("CanUploadToCloudClipboard", names);
    }

    [Theory]
    [InlineData("CanIncludeInClipboardHistory")]
    [InlineData("CanUploadToCloudClipboard")]
    public void OptOutFormats_CarryDwordZero(string formatName)
    {
        var payload = ClipboardPayload.ForSecret("x", Guid.NewGuid());
        var entry = payload.RegisteredFormats.Single(f => f.FormatName == formatName);

        Assert.Equal(new byte[] { 0, 0, 0, 0 }, entry.Data);
    }

    [Fact]
    public void ExcludeFormat_CarriesData()
    {
        var payload = ClipboardPayload.ForSecret("x", Guid.NewGuid());
        var entry = payload.RegisteredFormats.Single(f => f.FormatName == "ExcludeClipboardContentFromMonitorProcessing");

        Assert.NotEmpty(entry.Data);
    }

    [Fact]
    public void TokenFormat_CarriesTheToken()
    {
        var token = Guid.NewGuid();
        var payload = ClipboardPayload.ForSecret("x", token);
        var entry = payload.RegisteredFormats.Single(f => f.FormatName == ClipboardPayload.TokenFormat);

        Assert.Equal(token.ToByteArray(), entry.Data);
        Assert.Equal(token, payload.Token);
    }

    [Fact]
    public void UnicodeText_IsUtf16LittleEndianWithTerminator()
    {
        const string text = "  pässwörd 日本語 🔑 ";
        var payload = ClipboardPayload.ForSecret(text, Guid.NewGuid());

        var expected = Encoding.Unicode.GetBytes(text + "\0");
        Assert.Equal(expected, payload.UnicodeTextBytes);
        Assert.Equal(text, payload.Text);
    }

    [Fact]
    public void ToString_DoesNotRevealText()
    {
        var payload = ClipboardPayload.ForSecret("secret-架空-xyz", Guid.NewGuid());

        Assert.DoesNotContain("secret-架空", payload.ToString(), StringComparison.Ordinal);
    }
}
