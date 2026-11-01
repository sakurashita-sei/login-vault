using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using LoginVault.App.ViewModels;

namespace LoginVault.App.Views;

/// <summary>Account add/edit dialog. The password field value is handed to the view model only on save.</summary>
public partial class AccountEditorWindow : Window
{
    /// <summary>Upper limit of the window height: closed it fits its content; with "詳細設定" open it stops here and the form scrolls (D-045).</summary>
    public const double MaxContentHeight = 640;

    private readonly AccountEditorViewModel _editor;

    public AccountEditorWindow(AccountEditorViewModel editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        InitializeComponent();
        MaxHeight = MaxContentHeight;
        // 高さは内容に合わせる（詳細設定を開くと上限まで伸び、閉じると縮む）。画面より高くはせず、はみ出したら位置を戻す
        SourceInitialized += (_, _) =>
        {
            WindowSizing.FitToWorkArea(this);
            MaxHeight = Math.Min(MaxContentHeight, SystemParameters.WorkArea.Height);
        };
        SizeChanged += (_, _) => WindowSizing.KeepInWorkArea(this);
        DataContext = editor;
        if (editor.PendingNewPassword.Length > 0)
        {
            // 保存失敗後の再表示では入力済みの新しいパスワードを保持する
            NewPasswordField.Password = editor.PendingNewPassword;
        }

        // 新しいサービスで開いたとき（サービスが 1 件もない保管庫）はサービス名の入力欄から始める
        Loaded += (_, _) =>
        {
            if (editor.IsNewService)
            {
                NewServiceBox.Focus();
            }
            else
            {
                ServiceCombo.Focus();
            }
        };
        editor.PropertyChanged += OnEditorPropertyChanged;
        Closed += (_, _) => editor.PropertyChanged -= OnEditorPropertyChanged;
    }

    /// <summary>Choosing "＋ 新しいサービスを追加…" moves the focus to the new service name once the box is shown.</summary>
    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AccountEditorViewModel.IsNewService) && _editor.IsNewService)
        {
            // 一覧が閉じて入力欄が表示された後でないとフォーカスできないため、入力の後に回す
            _ = Dispatcher.InvokeAsync(() => NewServiceBox.Focus(), DispatcherPriority.Input);
        }
    }

    private async void Generate_Click(object sender, RoutedEventArgs e)
    {
        // 生成した値は伏せ字の欄に入れる（表示は目のアイコンで）。キャンセルなら何もしない
        var value = await _editor.GeneratePasswordAsync();
        if (value is not null)
        {
            NewPasswordField.Password = value;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _editor.SetNewPassword(NewPasswordField.Password);
        if (_editor.TryBuildInput(out _, out _))
        {
            DialogResult = true;
        }
    }
}
