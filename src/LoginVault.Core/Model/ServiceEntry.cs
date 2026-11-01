namespace LoginVault.Core.Model;

/// <summary>A service heading (Google, Amazon, a game, ...) under which accounts are grouped.</summary>
public sealed record ServiceEntry
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    // サービス名も暗号化対象のため、診断出力には ID だけを出す
    public override string ToString() => $"ServiceEntry({Id})";
}
