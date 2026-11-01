using System.Windows;

namespace LoginVault.App.Views;

/// <summary>Confirmation or information dialog with an optional detail list.</summary>
public partial class ConfirmWindow : Window
{
    public ConfirmWindow(string title, string message, IReadOnlyList<string>? details, string confirmText, bool isDestructive, bool showCancel)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
        if (details is { Count: > 0 })
        {
            DetailsList.ItemsSource = details;
        }
        else
        {
            DetailsPanel.Visibility = Visibility.Collapsed;
        }

        // 主操作はアクセント色。破壊的操作は標準の見た目にしてキャンセルを既定にする
        if (!isDestructive && showCancel)
        {
            ConfirmButton.Style = (Style)FindResource("FluentAccentButton");
        }

        CancelButton.Visibility = showCancel ? Visibility.Visible : Visibility.Collapsed;
        // 破壊的操作では Enter キーで実行されないよう、キャンセルを既定にする
        ConfirmButton.IsDefault = !isDestructive;
        CancelButton.IsDefault = isDestructive && showCancel;
        Loaded += (_, _) =>
        {
            if (isDestructive && showCancel)
            {
                CancelButton.Focus();
            }
            else
            {
                ConfirmButton.Focus();
            }
        };
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
