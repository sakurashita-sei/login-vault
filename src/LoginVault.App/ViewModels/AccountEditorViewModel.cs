using CommunityToolkit.Mvvm.ComponentModel;
using LoginVault.Core.Model;
using LoginVault.Core.Operations;
using LoginVault.Core.Validation;

namespace LoginVault.App.ViewModels;

public enum PasswordEditMode
{
    /// <summary>変更なし: keep the stored password.</summary>
    Keep,

    /// <summary>新しいパスワード: replace with the typed value.</summary>
    Set,

    /// <summary>明示的に空欄化: store an empty password.</summary>
    Clear,
}

public sealed record ServiceChoice(Guid Id, string Name)
{
    public override string ToString() => $"ServiceChoice({Id})";
}

public sealed record EmailCandidateItem(Guid AccountId, string Label)
{
    public override string ToString() => $"EmailCandidateItem({AccountId})";
}

/// <summary>State of the account editor dialog. Builds an <see cref="AccountInput"/>; it never saves by itself.</summary>
public sealed partial class AccountEditorViewModel : ObservableObject
{
    public const string EmailAccountQuestion = "この情報はメールアカウントのログイン情報ですか？";

    /// <summary>The last entry of the service list: choosing it shows a box for a new service name; the service is created with the account in the same save (D-048).</summary>
    public static readonly ServiceChoice NewServiceChoice = new(Guid.Empty, "＋ 新しいサービスを追加…");

    private readonly Guid? _editingAccountId;
    private string _newPassword = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNewService))]
    private ServiceChoice? _selectedService;

    /// <summary>Name of the service to create when <see cref="IsNewService"/>; an existing service with the same name is used instead.</summary>
    [ObservableProperty]
    private string _newServiceName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetailSettings), nameof(DetailsHeaderText))]
    private string _label = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetailSettings), nameof(DetailsHeaderText))]
    private bool _isEmailAccount;

    [ObservableProperty]
    private bool _useEmailReference;

    [ObservableProperty]
    private string? _loginIdText;

    [ObservableProperty]
    private EmailCandidateItem? _selectedEmailCandidate;

    [ObservableProperty]
    private PasswordEditMode _passwordMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetailSettings), nameof(DetailsHeaderText))]
    private MfaStatus _mfaStatus;

    [ObservableProperty]
    private bool _useSms;

    [ObservableProperty]
    private string _smsDetail = string.Empty;

    [ObservableProperty]
    private bool _useEmail;

    [ObservableProperty]
    private string _emailDetail = string.Empty;

    [ObservableProperty]
    private bool _useAuthenticator;

    [ObservableProperty]
    private string _authenticatorDetail = string.Empty;

    [ObservableProperty]
    private bool _useOther;

    [ObservableProperty]
    private string _otherName = string.Empty;

    [ObservableProperty]
    private string _otherDetail = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetailSettings), nameof(DetailsHeaderText))]
    private string _notes = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>Whether the collapsible "詳細設定" section is open. Closed by default for both new and existing accounts (D-045).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailsHeaderText))]
    private bool _isDetailsExpanded;

    // 詳細設定の中の項目に関する整合性エラー（表示時に詳細設定を開く）
    private static readonly HashSet<IssueCode> DetailIssueCodes = new()
    {
        IssueCode.AccountIsReferenced,
        IssueCode.InvalidEmailFormat,
        IssueCode.EmailAccountMustBeDirect,
        IssueCode.MfaMethodsRequiredWhenEnabled,
        IssueCode.MfaMethodsMustBeEmptyWhenNotEnabled,
        IssueCode.OtherMfaMethodNeedsCustomType,
        IssueCode.TooManyItems,
        IssueCode.ValueTooLong,
    };

    public AccountEditorViewModel(VaultDocument document, AccountEntry? existing, Guid? preselectedServiceId)
    {
        ArgumentNullException.ThrowIfNull(document);
        _editingAccountId = existing?.Id;
        IsNew = existing is null;
        Services = document.Services
            .OrderBy(s => s.Name, StringComparer.InvariantCultureIgnoreCase)
            .Select(s => new ServiceChoice(s.Id, s.Name))
            .ToList();
        EmailCandidates = VaultQueries.EmailCandidates(document)
            .Where(c => c.Id != _editingAccountId)
            .Select(c => new EmailCandidateItem(c.Id, VaultQueries.EmailCandidateLabel(document, c)))
            .ToList();

        ServiceOptions = Services.Prepend(NewServiceChoice).ToList();
        var serviceId = existing?.ServiceId ?? preselectedServiceId;
        // サービスがまだ 1 件もない保管庫では、最初から新しいサービスの入力で開く
        SelectedService = Services.FirstOrDefault(s => s.Id == serviceId) ?? (existing is null && Services.Count == 0 ? NewServiceChoice : null);
        PasswordMode = IsNew ? PasswordEditMode.Set : PasswordEditMode.Keep;

        if (existing is not null)
        {
            Label = existing.Label;
            IsEmailAccount = existing.IsEmailAccount;
            UseEmailReference = existing.LoginIdMode == LoginIdMode.EmailReference;
            LoginIdText = existing.LoginIdText;
            SelectedEmailCandidate = EmailCandidates.FirstOrDefault(c => c.AccountId == existing.LoginEmailAccountId);
            MfaStatus = existing.MfaStatus;
            foreach (var method in existing.MfaMethods)
            {
                switch (method.Type)
                {
                    case MfaMethodType.Sms:
                        UseSms = true;
                        SmsDetail = method.Detail ?? string.Empty;
                        break;
                    case MfaMethodType.Email:
                        UseEmail = true;
                        EmailDetail = method.Detail ?? string.Empty;
                        break;
                    case MfaMethodType.Authenticator:
                        UseAuthenticator = true;
                        AuthenticatorDetail = method.Detail ?? string.Empty;
                        break;
                    case MfaMethodType.Other:
                        UseOther = true;
                        OtherName = method.CustomType ?? string.Empty;
                        OtherDetail = method.Detail ?? string.Empty;
                        break;
                    default:
                        break;
                }
            }

            Notes = existing.Notes;
        }
    }

    public bool IsNew { get; }

    public string Title => IsNew ? "アカウントの追加" : "アカウントの編集";

    public IReadOnlyList<ServiceChoice> Services { get; }

    /// <summary>The service list of the dialog: <see cref="NewServiceChoice"/> at the top followed by every service.</summary>
    public IReadOnlyList<ServiceChoice> ServiceOptions { get; }

    /// <summary>True while the "new service" entry is chosen.</summary>
    public bool IsNewService => SelectedService == NewServiceChoice;

    public IReadOnlyList<EmailCandidateItem> EmailCandidates { get; }

    public bool HasEmailCandidates => EmailCandidates.Count > 0;

    /// <summary>Email accounts must type their address directly; the reference option is unavailable for them.</summary>
    public bool CanUseEmailReference => !IsEmailAccount && HasEmailCandidates;

    public bool UseDirectInput => !UseEmailReference;

    /// <summary>"変更なし" is only meaningful when editing an existing account.</summary>
    public bool CanKeepPassword => !IsNew;

    public bool IsPasswordEntryEnabled => PasswordMode == PasswordEditMode.Set;

    public bool IsMfaEnabled => MfaStatus == MfaStatus.Enabled;

    /// <summary>True when two-factor authentication is configured; setting true changes status to Enabled.</summary>
    public bool IsMfaConfigured
    {
        get => MfaStatus == MfaStatus.Enabled;
        set
        {
            if (value)
            {
                MfaStatus = MfaStatus.Enabled;
            }
        }
    }

    /// <summary>True when two-factor authentication is not configured or unverified; setting true changes status to NotConfigured.</summary>
    public bool IsMfaNotConfigured
    {
        get => MfaStatus != MfaStatus.Enabled;
        set
        {
            if (value)
            {
                MfaStatus = MfaStatus.NotConfigured;
            }
        }
    }

    public string EmailAccountQuestionText => EmailAccountQuestion;

    /// <summary>True when a field inside "詳細設定" differs from a new account's defaults (label, email account, two-factor status, notes).</summary>
    public bool HasDetailSettings =>
        !string.IsNullOrWhiteSpace(Label) || IsEmailAccount || MfaStatus == MfaStatus.Enabled || !string.IsNullOrWhiteSpace(Notes);

    /// <summary>Text of the section toggle; while closed it tells that something is set inside.</summary>
    public string DetailsHeaderText => !IsDetailsExpanded && HasDetailSettings ? "詳細設定（設定あり）" : "詳細設定";

    /// <summary>Shows errors from the core rules; errors about fields inside "詳細設定" open the section so the cause is visible.</summary>
    public void ShowError(string message, IEnumerable<ValidationIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        ErrorMessage = message;
        if (issues.Any(i => i.Severity == IssueSeverity.Error && DetailIssueCodes.Contains(i.Code)))
        {
            IsDetailsExpanded = true;
        }
    }

    /// <summary>The new password entered so far; used to refill the PasswordBox when the editor is shown again after a failed save.</summary>
    public string PendingNewPassword => _newPassword;

    /// <summary>Called by the view with the PasswordBox content (never bound through a string property in XAML).</summary>
    public void SetNewPassword(string value)
    {
        _newPassword = value ?? string.Empty;
    }

    /// <summary>Shows the password generator (wired by the vault view model); returns the accepted candidate or null.</summary>
    public Func<Task<string?>>? GeneratePasswordHandler { get; set; }

    /// <summary>Generator options chosen while editing that differ from the saved ones; persisted in the same save as the account.</summary>
    public PasswordGeneratorOptions? GeneratorOptionsToSave { get; set; }

    /// <summary>Runs the generator; an accepted candidate becomes the pending new password and the mode switches to "new password".</summary>
    public async Task<string?> GeneratePasswordAsync()
    {
        if (GeneratePasswordHandler is null)
        {
            return null;
        }

        var value = await GeneratePasswordHandler();
        if (value is null)
        {
            return null;
        }

        SetNewPassword(value);
        PasswordMode = PasswordEditMode.Set;
        return value;
    }

    /// <summary>Validates the form and produces the input for the core operations.</summary>
    public bool TryBuildInput(out AccountInput? input, out string? error)
    {
        input = null;
        error = null;
        Guid serviceId;
        if (IsNewService)
        {
            if (string.IsNullOrWhiteSpace(NewServiceName))
            {
                error = "新しいサービス名を入力してください。";
                return Fail(error);
            }

            // 新しいサービスの ID は保存時に決まる（同じ名前のサービスがあればそれを使う。VaultOperations.WithService）
            serviceId = Guid.Empty;
        }
        else if (SelectedService is null)
        {
            error = "所属サービスを選択してください。";
            return Fail(error);
        }
        else
        {
            serviceId = SelectedService.Id;
        }

        var mode = UseEmailReference && !IsEmailAccount ? LoginIdMode.EmailReference : LoginIdMode.Direct;
        if (mode == LoginIdMode.Direct)
        {
            if (string.IsNullOrWhiteSpace(LoginIdText))
            {
                error = "ログインIDを入力してください。";
                return Fail(error);
            }

            if (IsEmailAccount && !EmailAddressFormat.IsValid(LoginIdText))
            {
                error = "メールアカウントのログインIDはメールアドレスの形式で入力してください。";
                return Fail(error, inDetails: true);
            }
        }
        else if (SelectedEmailCandidate is null)
        {
            error = "登録済みメールから参照するメールアカウントを選択してください。";
            return Fail(error);
        }

        PasswordChange password;
        if (IsNew)
        {
            password = _newPassword.Length == 0 ? PasswordChange.Clear : PasswordChange.Set(_newPassword);
        }
        else
        {
            switch (PasswordMode)
            {
                case PasswordEditMode.Keep:
                    password = PasswordChange.Keep;
                    break;
                case PasswordEditMode.Clear:
                    password = PasswordChange.Clear;
                    break;
                default:
                    if (_newPassword.Length == 0)
                    {
                        error = "新しいパスワードを入力するか、「空欄にする」を選択してください。";
                        return Fail(error);
                    }

                    password = PasswordChange.Set(_newPassword);
                    break;
            }
        }

        var methods = new List<MfaMethod>();
        if (MfaStatus == MfaStatus.Enabled)
        {
            if (UseSms)
            {
                methods.Add(new MfaMethod(MfaMethodType.Sms, null, NullIfBlank(SmsDetail)));
            }

            if (UseEmail)
            {
                methods.Add(new MfaMethod(MfaMethodType.Email, null, NullIfBlank(EmailDetail)));
            }

            if (UseAuthenticator)
            {
                methods.Add(new MfaMethod(MfaMethodType.Authenticator, null, NullIfBlank(AuthenticatorDetail)));
            }

            if (UseOther)
            {
                if (string.IsNullOrWhiteSpace(OtherName))
                {
                    error = "方式「その他」の方式名を入力してください。";
                    return Fail(error, inDetails: true);
                }

                methods.Add(new MfaMethod(MfaMethodType.Other, OtherName.Trim(), NullIfBlank(OtherDetail)));
            }

            if (methods.Count == 0)
            {
                error = "二段階認証が「設定あり」の場合は方式を1つ以上選択してください。";
                return Fail(error, inDetails: true);
            }
        }

        input = new AccountInput
        {
            ServiceId = serviceId,
            Label = Label ?? string.Empty,
            LoginIdMode = mode,
            LoginIdText = mode == LoginIdMode.Direct ? LoginIdText : null,
            LoginEmailAccountId = mode == LoginIdMode.EmailReference ? SelectedEmailCandidate!.AccountId : null,
            Password = password,
            IsEmailAccount = IsEmailAccount,
            MfaStatus = MfaStatus,
            MfaMethods = methods,
            Notes = Notes ?? string.Empty,
        };
        ErrorMessage = null;
        return true;
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Shows the error; an error about a field inside "詳細設定" also opens the section.</summary>
    private bool Fail(string message, bool inDetails = false)
    {
        ErrorMessage = message;
        if (inDetails)
        {
            IsDetailsExpanded = true;
        }

        return false;
    }

    partial void OnIsEmailAccountChanged(bool value)
    {
        if (value)
        {
            UseEmailReference = false;
        }

        OnPropertyChanged(nameof(CanUseEmailReference));
    }

    partial void OnUseEmailReferenceChanged(bool value)
    {
        if (value && IsEmailAccount)
        {
            UseEmailReference = false;
            return;
        }

        OnPropertyChanged(nameof(UseDirectInput));
    }

    partial void OnPasswordModeChanged(PasswordEditMode value) => OnPropertyChanged(nameof(IsPasswordEntryEnabled));

    partial void OnMfaStatusChanged(MfaStatus value)
    {
        OnPropertyChanged(nameof(IsMfaEnabled));
        OnPropertyChanged(nameof(IsMfaConfigured));
        OnPropertyChanged(nameof(IsMfaNotConfigured));
    }
}
