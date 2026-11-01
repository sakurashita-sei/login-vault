using System.Windows;
using System.Windows.Controls;
using LoginVault.App.ViewModels;

namespace LoginVault.App.Views;

/// <summary>First-run screen. Reads the password fields here because WPF does not bind them.</summary>
public partial class SetupView : UserControl
{
    public SetupView()
    {
        InitializeComponent();
        Loaded += (_, _) => PasswordField.FocusInput();
    }

    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel shell)
        {
            return;
        }

        await shell.InitializeVaultAsync(PasswordField.Password, ConfirmField.Password);
        if (shell.State == ShellState.Unlocked)
        {
            PasswordField.Clear();
            ConfirmField.Clear();
        }
    }
}
