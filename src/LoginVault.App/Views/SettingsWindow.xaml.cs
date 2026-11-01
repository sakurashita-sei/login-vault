using System.Windows;
using System.Windows.Controls;
using LoginVault.App.ViewModels;

namespace LoginVault.App.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _settings;

    public SettingsWindow(SettingsViewModel settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        InitializeComponent();
        DataContext = settings;
        Loaded += (_, _) => LockBox.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // 数値に変換できない入力はバインディングが拒否して旧値のまま残るため、保存前に検出する
        if (Validation.GetHasError(LockBox) || Validation.GetHasError(RevealBox) || Validation.GetHasError(ClipboardBox) || Validation.GetHasError(BackupBox))
        {
            _settings.ErrorMessage = "数値を入力してください。";
            return;
        }

        if (_settings.TryBuild(out _, out _))
        {
            DialogResult = true;
        }
    }
}
