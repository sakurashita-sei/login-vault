using System.Windows;

namespace LoginVault.App.Views;

/// <summary>Hosts the setup, unlock or vault view depending on the shell state.</summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowSizing.FitToWorkArea(this);
    }
}
