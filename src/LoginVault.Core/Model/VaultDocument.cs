namespace LoginVault.Core.Model;

/// <summary>The whole decrypted content of a vault: services, accounts and preferences.</summary>
public sealed record VaultDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public IReadOnlyList<ServiceEntry> Services { get; init; } = Array.Empty<ServiceEntry>();

    public IReadOnlyList<AccountEntry> Accounts { get; init; } = Array.Empty<AccountEntry>();

    public VaultPreferences Preferences { get; init; } = new();

    public static VaultDocument Empty { get; } = new();

    // 件数だけを出し、中身は出力しない
    public override string ToString() => $"VaultDocument(schema={SchemaVersion}, services={Services.Count}, accounts={Accounts.Count})";
}
