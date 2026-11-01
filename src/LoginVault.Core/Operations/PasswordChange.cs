namespace LoginVault.Core.Operations;

/// <summary>
/// Describes what should happen to an account password on save, so that an empty
/// input field never silently erases an existing password.
/// </summary>
public abstract record PasswordChange
{
    private PasswordChange()
    {
    }

    /// <summary>Leave the stored password untouched.</summary>
    public static PasswordChange Keep { get; } = new KeepPassword();

    /// <summary>Explicitly store an empty password.</summary>
    public static PasswordChange Clear { get; } = new ClearPassword();

    /// <summary>Store a new value exactly as given (no trimming or normalization).</summary>
    public static PasswordChange Set(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new SetPassword(value);
    }

    /// <summary>Applies this change to the currently stored password.</summary>
    public string Resolve(string existing) => this switch
    {
        KeepPassword => existing,
        ClearPassword => string.Empty,
        SetPassword set => set.Value,
        _ => throw new InvalidOperationException("未知のパスワード変更種別です。"),
    };

    // 派生 record が値を含む ToString を生成しないよう sealed で固定する
    public sealed override string ToString() => GetType().Name;

    public sealed record KeepPassword : PasswordChange;

    public sealed record ClearPassword : PasswordChange;

    public sealed record SetPassword(string Value) : PasswordChange;
}
