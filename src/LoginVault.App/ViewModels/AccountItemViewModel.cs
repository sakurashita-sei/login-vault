using CommunityToolkit.Mvvm.ComponentModel;
using LoginVault.Core.Model;
using LoginVault.Core.Operations;

namespace LoginVault.App.ViewModels;

/// <summary>One account row in the right pane. Holds the password for copy/reveal while the vault is unlocked.</summary>
public sealed partial class AccountItemViewModel : ObservableObject
{
    /// <summary>Fixed-length mask shown regardless of the real password length.</summary>
    public const string Mask = "●●●●●●●●";

    [ObservableProperty]
    private bool _isRevealed;

    public AccountItemViewModel(AccountEntry account, VaultDocument document)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(document);
        Id = account.Id;
        ServiceId = account.ServiceId;
        ServiceName = VaultQueries.FindService(document, account.ServiceId)?.Name ?? "（不明なサービス）";
        DisplayName = VaultQueries.DisplayName(document, account);
        var resolution = VaultQueries.ResolveLoginId(document, account);
        IsBroken = resolution.IsBroken;
        LoginId = resolution.IsBroken ? null : resolution.Value;
        LoginIdDisplay = resolution.IsBroken ? VaultQueries.BrokenReferenceText : (resolution.Value ?? string.Empty);
        IsReference = account.LoginIdMode == LoginIdMode.EmailReference;
        if (IsReference && account.LoginEmailAccountId is { } targetId && VaultQueries.FindAccount(document, targetId) is { } target)
        {
            var targetService = VaultQueries.FindService(document, target.ServiceId)?.Name ?? "（不明なサービス）";
            ReferenceSourceText = string.IsNullOrWhiteSpace(target.Label)
                ? $"{targetService} のメールアドレスを参照"
                : $"{targetService}／{target.Label} のメールアドレスを参照";
        }
        else if (IsReference)
        {
            ReferenceSourceText = "参照先のメールアカウントが見つかりません";
        }
        else
        {
            ReferenceSourceText = string.Empty;
        }

        IsEmailAccount = account.IsEmailAccount;
        Password = account.Password;
        var methods = string.Join("、", account.MfaMethods.Select(DisplayText.Summary));
        MfaText = account.MfaStatus == MfaStatus.Enabled && methods.Length > 0
            ? $"二段階認証：{DisplayText.Of(account.MfaStatus)}（{methods}）"
            : $"二段階認証：{DisplayText.Of(account.MfaStatus)}";
        Notes = account.Notes;
        UpdatedAtText = $"更新 {account.UpdatedAt.ToLocalTime():yyyy/MM/dd HH:mm}";
    }

    public Guid Id { get; }

    public Guid ServiceId { get; }

    public string ServiceName { get; }

    public string DisplayName { get; }

    /// <summary>The effective login ID, or null when the reference is broken.</summary>
    public string? LoginId { get; }

    public string LoginIdDisplay { get; }

    public bool IsReference { get; }

    public bool IsBroken { get; }

    public string ReferenceSourceText { get; }

    public bool HasReferenceSource => ReferenceSourceText.Length > 0;

    public bool IsEmailAccount { get; }

    public string MfaText { get; }

    public string Notes { get; }

    public bool HasNotes => Notes.Length > 0;

    public string UpdatedAtText { get; }

    public string PasswordDisplay => IsRevealed ? Password : Mask;

    public string RevealButtonText => IsRevealed ? "隠す" : "表示";

    internal string Password { get; }

    public override string ToString() => $"AccountItemViewModel({Id})";

    partial void OnIsRevealedChanged(bool value)
    {
        OnPropertyChanged(nameof(PasswordDisplay));
        OnPropertyChanged(nameof(RevealButtonText));
    }
}
