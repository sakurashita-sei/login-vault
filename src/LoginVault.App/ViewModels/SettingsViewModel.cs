using CommunityToolkit.Mvvm.ComponentModel;
using LoginVault.Core.Model;

namespace LoginVault.App.ViewModels;

/// <summary>Editable copy of <see cref="VaultPreferences"/> with range validation.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty]
    private int _autoLockMinutes;

    [ObservableProperty]
    private int _revealSeconds;

    [ObservableProperty]
    private int _clipboardClearSeconds;

    [ObservableProperty]
    private int _backupGenerations;

    [ObservableProperty]
    private string? _errorMessage;

    private readonly VaultPreferences _original;

    public SettingsViewModel(VaultPreferences preferences, string dataDirectory)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        _original = preferences;
        AutoLockMinutes = preferences.AutoLockMinutes;
        RevealSeconds = preferences.RevealSeconds;
        ClipboardClearSeconds = preferences.ClipboardClearSeconds;
        BackupGenerations = preferences.BackupGenerations;
        DataDirectory = dataDirectory;
    }

    public string DataDirectory { get; }

    public string AutoLockRangeText => $"{VaultPreferences.MinAutoLockMinutes}〜{VaultPreferences.MaxAutoLockMinutes} 分";

    public string RevealRangeText => $"{VaultPreferences.MinRevealSeconds}〜{VaultPreferences.MaxRevealSeconds} 秒";

    public string ClipboardRangeText => $"{VaultPreferences.MinClipboardClearSeconds}〜{VaultPreferences.MaxClipboardClearSeconds} 秒";

    public string BackupRangeText => $"{VaultPreferences.MinBackupGenerations}〜{VaultPreferences.MaxBackupGenerations} 世代";

    public bool TryBuild(out VaultPreferences? preferences, out string? error)
    {
        // ダイアログにない設定（画面キャプチャ除外など）は元の値を保つ
        preferences = _original with
        {
            AutoLockMinutes = AutoLockMinutes,
            RevealSeconds = RevealSeconds,
            ClipboardClearSeconds = ClipboardClearSeconds,
            BackupGenerations = BackupGenerations,
        };
        if (!preferences.IsValid)
        {
            preferences = null;
            error = "設定値が許容範囲外です。各項目の範囲を確認してください。";
            ErrorMessage = error;
            return false;
        }

        error = null;
        ErrorMessage = null;
        return true;
    }
}
