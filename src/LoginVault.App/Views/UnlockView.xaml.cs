using System.Windows;
using System.Windows.Controls;
using LoginVault.App.ViewModels;

namespace LoginVault.App.Views;

/// <summary>Locked screen.</summary>
public partial class UnlockView : UserControl
{
    public UnlockView()
    {
        InitializeComponent();
        Loaded += (_, _) => PasswordField.FocusInput();
    }

    private async void Unlock_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel shell)
        {
            return;
        }

        var password = PasswordField.Password;
        PasswordField.Clear();
        await shell.UnlockAsync(password);
        if (shell.State != ShellState.Unlocked)
        {
            PasswordField.FocusInput();
        }
    }
}
