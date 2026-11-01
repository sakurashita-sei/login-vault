using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LoginVault.App.ViewModels;

namespace LoginVault.App.Views;

/// <summary>Password generator dialog. Returns true when the user takes the current candidate (held by the view model).</summary>
public partial class PasswordGeneratorWindow : Window
{
    private readonly PasswordGeneratorViewModel _generator;

    public PasswordGeneratorWindow(PasswordGeneratorViewModel generator)
    {
        _generator = generator ?? throw new ArgumentNullException(nameof(generator));
        InitializeComponent();
        DataContext = generator;
        Loaded += (_, _) => UseButton.Focus();
    }

    private void Use_Click(object sender, RoutedEventArgs e)
    {
        if (_generator.CanAccept)
        {
            DialogResult = true;
        }
    }

    private void CandidateBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            textBox.Focus();
            textBox.SelectAll();
            e.Handled = true;
        }
    }

    private void CandidateBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            textBox.SelectAll();
        }
    }
}
