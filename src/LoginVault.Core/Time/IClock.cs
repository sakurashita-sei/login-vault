namespace LoginVault.Core.Time;

/// <summary>Abstraction over the current time so that tests can control timestamps and timers.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>The real system clock.</summary>
public sealed class SystemClock : IClock
{
    public static SystemClock Instance { get; } = new();

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
