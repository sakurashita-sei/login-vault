using System.Windows;
using LoginVault.App.Services;

namespace LoginVault.App.Views;

public partial class ChangeMasterPasswordWindow : Window
{
    public ChangeMasterPasswordWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => CurrentField.FocusInput();
    }

    public ChangeMasterPasswordRequest? Request { get; private set; }

    private void Change_Click(object sender, RoutedEventArgs e)
    {
        if (NewField.Password.Length == 0)
        {
            ShowError("新しいマスターパスワードを入力してください。");
            return;
        }

        if (!string.Equals(NewField.Password, ConfirmField.Password, StringComparison.Ordinal))
        {
            ShowError("確認用の新しいマスターパスワードが一致しません。");
            return;
        }

        Request = new ChangeMasterPasswordRequest(CurrentField.Password, NewField.Password);
        DialogResult = true;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
