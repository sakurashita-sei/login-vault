using System.Windows;
using LoginVault.App.ViewModels;

namespace LoginVault.App.Views;

public partial class EmailChangeImpactWindow : Window
{
    public EmailChangeImpactWindow(EmailChangeImpactViewModel impact)
    {
        InitializeComponent();
        DataContext = impact;
    }

    private void Proceed_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
