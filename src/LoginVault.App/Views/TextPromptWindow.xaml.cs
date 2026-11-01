using System.Windows;

namespace LoginVault.App.Views;

/// <summary>Single-line text prompt (service names).</summary>
public partial class TextPromptWindow : Window
{
    public TextPromptWindow(string title, string message, string? initialValue)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
        InputBox.Text = initialValue ?? string.Empty;
        Loaded += (_, _) =>
        {
            InputBox.Focus();
            InputBox.SelectAll();
        };
    }

    public string Value => InputBox.Text;

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
