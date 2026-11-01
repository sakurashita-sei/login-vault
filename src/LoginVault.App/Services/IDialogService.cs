using LoginVault.App.ViewModels;

namespace LoginVault.App.Services;

/// <summary>Current and new master password entered in the change dialog.</summary>
public sealed record ChangeMasterPasswordRequest(string CurrentPassword, string NewPassword)
{
    public override string ToString() => "ChangeMasterPasswordRequest";
}

/// <summary>Backup file and the password that opens it.</summary>
public sealed record RestoreRequest(string FilePath, string Password)
{
    public override string ToString() => "RestoreRequest";
}

/// <summary>All modal interaction the view models need; the WPF implementation shows windows, tests script answers.</summary>
public interface IDialogService
{
    Task<string?> PromptTextAsync(string title, string message, string? initialValue);

    Task<bool> ConfirmAsync(string title, string message, IReadOnlyList<string>? details = null, string confirmText = "OK", bool isDestructive = false);

    Task ShowMessageAsync(string title, string message, IReadOnlyList<string>? details = null);

    /// <summary>Returns true when the user chose to save; the editor then holds valid input.</summary>
    Task<bool> ShowAccountEditorAsync(AccountEditorViewModel editor);

    /// <summary>Returns true to proceed with the address change (choices are recorded on the view model).</summary>
    Task<bool> ShowEmailChangeImpactAsync(EmailChangeImpactViewModel impact);

    Task<bool> ShowSettingsAsync(SettingsViewModel settings);

    /// <summary>Returns true when the user takes the generated candidate (the view model holds it and the chosen options).</summary>
    Task<bool> ShowPasswordGeneratorAsync(PasswordGeneratorViewModel generator);

    Task<ChangeMasterPasswordRequest?> ShowChangeMasterPasswordAsync();

    Task<RestoreRequest?> ShowRestoreAsync();

    Task<string?> PickBackupDestinationAsync(string suggestedFileName);

    /// <summary>Closes every open dialog without saving (used when the vault locks).</summary>
    void CloseAll();
}
