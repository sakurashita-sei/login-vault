using CommunityToolkit.Mvvm.ComponentModel;
using LoginVault.Core.Model;
using LoginVault.Core.Operations;

namespace LoginVault.App.ViewModels;

/// <summary>An account that references the email being changed, with the user's choice.</summary>
public sealed partial class ReferrerChoice : ObservableObject
{
    [ObservableProperty]
    private bool _convertToDirect;

    public ReferrerChoice(Guid accountId, string display)
    {
        AccountId = accountId;
        Display = display;
    }

    public Guid AccountId { get; }

    public string Display { get; }
}

/// <summary>Shown before an email account's address changes while other accounts reference it.</summary>
public sealed class EmailChangeImpactViewModel
{
    public const string Warning =
        "Login Vault 内でメールアドレスを変更しても、実際の各サービス側に登録されているメールアドレスは自動では変更されません。" +
        "各サービスで変更が済んでいるか確認してください。まだ旧アドレスのままのサービスは「直接入力に変換」にチェックすると、旧アドレスを保持したまま参照を解除します。";

    public EmailChangeImpactViewModel(VaultDocument document, string oldAddress, string newAddress, IReadOnlyList<AccountEntry> referrers)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(referrers);
        OldAddress = oldAddress;
        NewAddress = newAddress;
        Referrers = referrers
            .Select(r => new ReferrerChoice(r.Id, $"{VaultQueries.FindService(document, r.ServiceId)?.Name ?? "（不明なサービス）"}／{VaultQueries.DisplayName(document, r)}"))
            .ToList();
    }

    public string OldAddress { get; }

    public string NewAddress { get; }

    public string WarningText => Warning;

    public IReadOnlyList<ReferrerChoice> Referrers { get; }

    public string Summary => $"「{OldAddress}」を「{NewAddress}」に変更します。このメールアドレスをログインIDとして参照しているアカウントが{Referrers.Count}件あります。";
}
