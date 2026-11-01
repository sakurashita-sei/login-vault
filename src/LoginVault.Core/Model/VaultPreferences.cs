using System.Text.Json.Serialization;

namespace LoginVault.Core.Model;

/// <summary>User preferences stored inside the encrypted vault.</summary>
public sealed record VaultPreferences
{
    public const int MinAutoLockMinutes = 1;
    public const int MaxAutoLockMinutes = 60;
    public const int MinRevealSeconds = 5;
    public const int MaxRevealSeconds = 120;
    public const int MinClipboardClearSeconds = 5;
    public const int MaxClipboardClearSeconds = 300;
    public const int MinBackupGenerations = 1;
    public const int MaxBackupGenerations = 50;

    /// <summary>Idle minutes before the vault locks itself. Design default: 5.</summary>
    public int AutoLockMinutes { get; init; } = 5;

    /// <summary>Seconds a revealed password stays visible. Design default: 15.</summary>
    public int RevealSeconds { get; init; } = 15;

    /// <summary>Seconds after a copy before the clipboard is cleared if it still holds our copy. Design default: 30.</summary>
    public int ClipboardClearSeconds { get; init; } = 30;

    /// <summary>Number of automatic backup generations to keep. Design default: 10.</summary>
    public int BackupGenerations { get; init; } = 10;

    /// <summary>
    /// Hide the app's windows from screen capture (screen sharing, recording, screenshots) via SetWindowDisplayAffinity.
    /// Off by default; documents written before this field existed read as off.
    /// </summary>
    public bool ExcludeFromScreenCapture { get; init; }

    /// <summary>Last used options of the password generator (defaults for documents written before the generator existed).</summary>
    public PasswordGeneratorOptions PasswordGenerator { get; init; } = new();

    [JsonIgnore]
    public bool IsValid =>
        AutoLockMinutes is >= MinAutoLockMinutes and <= MaxAutoLockMinutes
        && RevealSeconds is >= MinRevealSeconds and <= MaxRevealSeconds
        && ClipboardClearSeconds is >= MinClipboardClearSeconds and <= MaxClipboardClearSeconds
        && BackupGenerations is >= MinBackupGenerations and <= MaxBackupGenerations
        && PasswordGenerator is { IsValid: true };
}
