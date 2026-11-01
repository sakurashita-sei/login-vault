namespace LoginVault.App.Clipboard;

public enum ClipboardSetResult
{
    Success,

    /// <summary>Another process holds the clipboard open; retrying may succeed.</summary>
    Busy,

    Failed,
}

public enum ClipboardClearResult
{
    /// <summary>Our copy was still there and has been removed.</summary>
    Cleared,

    /// <summary>The clipboard holds something else (or nothing); it was left untouched.</summary>
    NotOurs,

    Busy,

    Failed,
}

/// <summary>The platform clipboard. Implementations must check the token while holding the clipboard open so that check and clear cannot race.</summary>
public interface IClipboardDevice
{
    ClipboardSetResult TrySet(ClipboardPayload payload);

    ClipboardClearResult TryClearIfToken(Guid token);
}
