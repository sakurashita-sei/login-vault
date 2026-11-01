using System.Text;

namespace LoginVault.App.Clipboard;

/// <summary>One registered clipboard format and its raw bytes.</summary>
public sealed record ClipboardFormatEntry(string FormatName, byte[] Data)
{
    public override string ToString() => $"ClipboardFormatEntry({FormatName}, {Data.Length} bytes)";
}

/// <summary>
/// Everything placed on the clipboard for one copy: the text as CF_UNICODETEXT, the Windows opt-out
/// formats for clipboard history and cloud sync, and a private token that identifies our copy.
/// Format names and DWORD values follow the official "Clipboard Formats" documentation.
/// </summary>
public sealed class ClipboardPayload
{
    public const string ExcludeFromMonitorProcessingFormat = "ExcludeClipboardContentFromMonitorProcessing";
    public const string CanIncludeInClipboardHistoryFormat = "CanIncludeInClipboardHistory";
    public const string CanUploadToCloudClipboardFormat = "CanUploadToCloudClipboard";
    public const string TokenFormat = "LoginVault.CopyToken";

    private static readonly byte[] DwordZero = { 0, 0, 0, 0 };

    private ClipboardPayload(string text, Guid token, IReadOnlyList<ClipboardFormatEntry> registeredFormats)
    {
        Text = text;
        Token = token;
        RegisteredFormats = registeredFormats;
    }

    public string Text { get; }

    public Guid Token { get; }

    /// <summary>Formats to register with RegisterClipboardFormat and place alongside CF_UNICODETEXT.</summary>
    public IReadOnlyList<ClipboardFormatEntry> RegisteredFormats { get; }

    /// <summary>CF_UNICODETEXT bytes: UTF-16LE with a terminating NUL.</summary>
    public byte[] UnicodeTextBytes => Encoding.Unicode.GetBytes(Text + "\0");

    public static ClipboardPayload ForSecret(string text, Guid token)
    {
        ArgumentNullException.ThrowIfNull(text);
        var formats = new List<ClipboardFormatEntry>
        {
            // データを置くだけで履歴・同期の対象外になる
            new(ExcludeFromMonitorProcessingFormat, (byte[])DwordZero.Clone()),
            // DWORD 0 = クリップボード履歴に含めない
            new(CanIncludeInClipboardHistoryFormat, (byte[])DwordZero.Clone()),
            // DWORD 0 = 他デバイスへ同期しない
            new(CanUploadToCloudClipboardFormat, (byte[])DwordZero.Clone()),
            new(TokenFormat, token.ToByteArray()),
        };
        return new ClipboardPayload(text, token, formats);
    }

    public override string ToString() => $"ClipboardPayload({Token})";
}
