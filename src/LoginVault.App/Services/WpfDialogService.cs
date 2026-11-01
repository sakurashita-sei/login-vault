using System.Windows;
using LoginVault.App.ViewModels;
using LoginVault.App.Views;
using Microsoft.Win32;

namespace LoginVault.App.Services;

/// <summary>Shows modal windows owned by the main window and can close them all when the vault locks.</summary>
public sealed class WpfDialogService : IDialogService
{
    private readonly Func<Window?> _owner;
    private readonly Action? _notifyActivity;
    private readonly List<Window> _open = new();
    private bool _closingAll;

    /// <param name="owner">The main window (dialogs are owned by it).</param>
    /// <param name="notifyActivity">Called around Win32 common dialogs, whose input WPF cannot observe, so they count as activity.</param>
    public WpfDialogService(Func<Window?> owner, Action? notifyActivity = null)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _notifyActivity = notifyActivity;
    }

    public Task<string?> PromptTextAsync(string title, string message, string? initialValue)
    {
        var window = new TextPromptWindow(title, message, initialValue);
        return Task.FromResult(ShowModal(window) ? window.Value : null);
    }

    public Task<bool> ConfirmAsync(string title, string message, IReadOnlyList<string>? details = null, string confirmText = "OK", bool isDestructive = false) =>
        Task.FromResult(ShowModal(new ConfirmWindow(title, message, details, confirmText, isDestructive, showCancel: true)));

    public Task ShowMessageAsync(string title, string message, IReadOnlyList<string>? details = null)
    {
        ShowModal(new ConfirmWindow(title, message, details, "OK", isDestructive: false, showCancel: false));
        return Task.CompletedTask;
    }

    public Task<bool> ShowAccountEditorAsync(AccountEditorViewModel editor) => Task.FromResult(ShowModal(new AccountEditorWindow(editor)));

    public Task<bool> ShowEmailChangeImpactAsync(EmailChangeImpactViewModel impact) => Task.FromResult(ShowModal(new EmailChangeImpactWindow(impact)));

    public Task<bool> ShowSettingsAsync(SettingsViewModel settings) => Task.FromResult(ShowModal(new SettingsWindow(settings)));

    public Task<bool> ShowPasswordGeneratorAsync(PasswordGeneratorViewModel generator) => Task.FromResult(ShowModal(new PasswordGeneratorWindow(generator)));

    public Task<ChangeMasterPasswordRequest?> ShowChangeMasterPasswordAsync()
    {
        var window = new ChangeMasterPasswordWindow();
        return Task.FromResult(ShowModal(window) ? window.Request : null);
    }

    public Task<RestoreRequest?> ShowRestoreAsync()
    {
        var window = new RestoreWindow(_notifyActivity);
        return Task.FromResult(ShowModal(window) ? window.Request : null);
    }

    public Task<string?> PickBackupDestinationAsync(string suggestedFileName)
    {
        var dialog = new SaveFileDialog
        {
            Title = "バックアップの保存先",
            FileName = suggestedFileName,
            DefaultExt = ".dat",
            Filter = "Login Vault 保管庫 (*.dat)|*.dat",
            OverwritePrompt = true,
            RestoreDirectory = true,
        };
        var owner = _owner();
        _notifyActivity?.Invoke();
        var accepted = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
        _notifyActivity?.Invoke();
        return Task.FromResult(accepted == true ? dialog.FileName : null);
    }

    public void CloseAll()
    {
        // 新しい順に閉じる。モーダルは DialogResult=false で「保存せず閉じる」扱いになる。
        // 閉じられたダイアログから同期的に戻った処理が新しいモーダルを開こうとしても、閉鎖中は開かない
        _closingAll = true;
        try
        {
            foreach (var window in _open.AsEnumerable().Reverse().ToList())
            {
                try
                {
                    window.DialogResult = false;
                }
                catch (InvalidOperationException)
                {
                    window.Close();
                }
            }
        }
        finally
        {
            _closingAll = false;
        }
    }

    private bool ShowModal(Window window)
    {
        if (_closingAll)
        {
            return false;
        }

        window.Owner = _owner();
        _open.Add(window);
        try
        {
            return window.ShowDialog() == true;
        }
        finally
        {
            _open.Remove(window);
        }
    }
}
