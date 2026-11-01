using System.Windows;
using System.Windows.Controls;

namespace LoginVault.App.Controls;

/// <summary>
/// Password entry with a reveal toggle inside the field. Uses the standard PasswordBox while hidden;
/// the plain TextBox is only populated while revealed and is cleared when hidden again.
/// </summary>
public partial class PasswordEntryField : UserControl
{
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(PasswordEntryField), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty AutomationNameProperty =
        DependencyProperty.Register(nameof(AutomationName), typeof(string), typeof(PasswordEntryField), new PropertyMetadata("パスワード", OnAutomationNameChanged));

    public static readonly DependencyProperty PlainAutomationNameProperty =
        DependencyProperty.Register(nameof(PlainAutomationName), typeof(string), typeof(PasswordEntryField), new PropertyMetadata("パスワード（表示中）"));

    public static readonly DependencyProperty IsRevealedProperty =
        DependencyProperty.Register(nameof(IsRevealed), typeof(bool), typeof(PasswordEntryField), new PropertyMetadata(false));

    private bool _syncing;

    public PasswordEntryField()
    {
        InitializeComponent();
        // 表示中の平文からクリップボードへ出す経路は XAML の ClipboardExport.IsBlocked で塞ぐ（コピーは SecretField の除外形式付きボタンだけ）
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>Raised whenever the entered value changes (either box).</summary>
    public event EventHandler? PasswordChanged;

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string AutomationName
    {
        get => (string)GetValue(AutomationNameProperty);
        set => SetValue(AutomationNameProperty, value);
    }

    public string PlainAutomationName
    {
        get => (string)GetValue(PlainAutomationNameProperty);
        set => SetValue(PlainAutomationNameProperty, value);
    }

    public bool IsRevealed
    {
        get => (bool)GetValue(IsRevealedProperty);
        private set => SetValue(IsRevealedProperty, value);
    }

    /// <summary>The entered password exactly as typed. Setting it fills the active box.</summary>
    public string Password
    {
        get => IsRevealed ? PlainBox.Text : PasswordBox.Password;
        set
        {
            _syncing = true;
            try
            {
                PasswordBox.Password = value ?? string.Empty;
                PlainBox.Text = IsRevealed ? value ?? string.Empty : string.Empty;
            }
            finally
            {
                _syncing = false;
            }

            PasswordChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Clear() => Password = string.Empty;

    /// <summary>Moves keyboard focus into the active box.</summary>
    public bool FocusInput() => IsRevealed ? PlainBox.Focus() : PasswordBox.Focus();

    private static void OnAutomationNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PasswordEntryField field && e.NewValue is string name)
        {
            field.PlainAutomationName = name + "（表示中）";
        }
    }

    private void Reveal_Click(object sender, RoutedEventArgs e)
    {
        _syncing = true;
        try
        {
            if (IsRevealed)
            {
                // 隠すときは平文の TextBox を空にし、PasswordBox 側だけに値を残す
                PasswordBox.Password = PlainBox.Text;
                PlainBox.Text = string.Empty;
                IsRevealed = false;
                PlainBox.Visibility = Visibility.Collapsed;
                PasswordBox.Visibility = Visibility.Visible;
                PasswordBox.Focus();
            }
            else
            {
                PlainBox.Text = PasswordBox.Password;
                IsRevealed = true;
                PasswordBox.Visibility = Visibility.Collapsed;
                PlainBox.Visibility = Visibility.Visible;
                PlainBox.Focus();
                PlainBox.CaretIndex = PlainBox.Text.Length;
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (!_syncing)
        {
            PasswordChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void PlainBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_syncing)
        {
            PasswordChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private Window? _window;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _window = Window.GetWindow(this);
        if (_window is not null)
        {
            _window.Deactivated -= OnWindowDeactivated;
            _window.Deactivated += OnWindowDeactivated;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_window is not null)
        {
            _window.Deactivated -= OnWindowDeactivated;
            _window = null;
        }
    }

    /// <summary>Hides a revealed password when the window loses activation (same rule as SecretField).</summary>
    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        if (IsRevealed)
        {
            Reveal_Click(this, new RoutedEventArgs());
        }
    }
}
