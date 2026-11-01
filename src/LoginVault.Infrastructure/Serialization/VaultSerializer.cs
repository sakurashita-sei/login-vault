using System.Text.Json;
using System.Text.Json.Serialization;
using LoginVault.Core.Model;
using LoginVault.Core.Validation;

namespace LoginVault.Infrastructure.Serialization;

/// <summary>The decrypted content is not a valid vault document (malformed JSON or integrity rule violations).</summary>
public sealed class VaultIntegrityException : Exception
{
    public VaultIntegrityException(string message, IReadOnlyList<ValidationIssue> issues, Exception? innerException = null)
        : base(message, innerException)
    {
        Issues = issues;
    }

    public IReadOnlyList<ValidationIssue> Issues { get; }
}

/// <summary>In-memory JSON serialization of the vault document. Never writes to disk.</summary>
public static class VaultSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.General)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // 列挙値は名前だけを受け付け、未定義の整数値を取り込まない
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
        WriteIndented = false,
    };

    public static byte[] Serialize(VaultDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return JsonSerializer.SerializeToUtf8Bytes(document, Options);
    }

    /// <summary>Parses and validates. Documents with integrity errors (broken references, bad MFA state, ...) are rejected rather than repaired.</summary>
    public static VaultDocument Deserialize(ReadOnlySpan<byte> utf8Json)
    {
        VaultDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<VaultDocument>(utf8Json, Options);
        }
        catch (JsonException ex)
        {
            throw new VaultIntegrityException("保管庫の内容を解釈できません。", Array.Empty<ValidationIssue>(), ex);
        }
        catch (NotSupportedException ex)
        {
            throw new VaultIntegrityException("保管庫の内容を解釈できません。", Array.Empty<ValidationIssue>(), ex);
        }

        if (document is null)
        {
            throw new VaultIntegrityException("保管庫の内容が空です。", Array.Empty<ValidationIssue>());
        }

        var report = VaultValidator.Validate(document);
        if (report.HasErrors)
        {
            throw new VaultIntegrityException("保管庫の内容に整合性エラーがあります。", report.Errors);
        }

        return document;
    }
}
