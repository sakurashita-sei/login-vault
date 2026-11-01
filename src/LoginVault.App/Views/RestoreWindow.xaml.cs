using System.IO;
using System.Windows;
using LoginVault.App.Services;
using Microsoft.Win32;

namespace LoginVault.App.Views;

public partial class RestoreWindow : Window
{
    private readonly Action? _notifyActivity;

    public RestoreWindow(Action? notifyActivity = null)
    {
        _notifyActivity = notifyActivity;
        InitializeComponent();
        Loaded += (_, _) => PathBox.Focus();
    }

    public RestoreRequest? Request { get; private set; }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "バックアップファイルを選択",
            Filter = "Login Vault 保管庫 (*.dat)|*.dat|すべてのファイル (*.*)|*.*",
            CheckFileExists = true,
            RestoreDirectory = true,
        };
        _notifyActivity?.Invoke();
        var accepted = dialog.ShowDialog(this) == true;
        _notifyActivity?.Invoke();
        if (accepted)
        {
            PathBox.Text = dialog.FileName;
        }
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        var path = PathBox.Text.Trim();
        if (path.Length == 0 || !File.Exists(path))
        {
            ErrorText.Text = "存在するバックアップファイルを指定してください。";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        if (PasswordField.Password.Length == 0)
        {
            ErrorText.Text = "バックアップのマスターパスワードを入力してください。";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        Request = new RestoreRequest(path, PasswordField.Password);
        DialogResult = true;
    }
}
