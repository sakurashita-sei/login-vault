using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace LoginVault.App.Controls;

/// <summary>
/// Fluent-style read-only field with trailing icon buttons: optional reveal toggle and copy.
/// The value is displayed in a non-selectable TextBlock so the only copy path is the guarded one.
/// </summary>
public partial class SecretField : UserControl
{
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(SecretField), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(SecretField), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ShowRevealButtonProperty =
        DependencyProperty.Register(nameof(ShowRevealButton), typeof(bool), typeof(SecretField), new PropertyMetadata(false));

    public static readonly DependencyProperty IsRevealedProperty =
        DependencyProperty.Register(nameof(IsRevealed), typeof(bool), typeof(SecretField), new PropertyMetadata(false));

    public static readonly DependencyProperty RevealCommandProperty =
        DependencyProperty.Register(nameof(RevealCommand), typeof(ICommand), typeof(SecretField), new PropertyMetadata(null));

    public static readonly DependencyProperty CopyCommandProperty =
        DependencyProperty.Register(nameof(CopyCommand), typeof(ICommand), typeof(SecretField), new PropertyMetadata(null));

    public static readonly DependencyProperty CommandParameterProperty =
        DependencyProperty.Register(nameof(CommandParameter), typeof(object), typeof(SecretField), new PropertyMetadata(null));

    public static readonly DependencyProperty CopyToolTipProperty =
        DependencyProperty.Register(nameof(CopyToolTip), typeof(string), typeof(SecretField), new PropertyMetadata("コピー（一定時間後に自動消去。クリップボード履歴・同期の対象外）"));

    public static readonly DependencyProperty CopyAutomationNameProperty =
        DependencyProperty.Register(nameof(CopyAutomationName), typeof(string), typeof(SecretField), new PropertyMetadata("コピー"));

    public static readonly DependencyProperty RevealAutomationNameProperty =
        DependencyProperty.Register(nameof(RevealAutomationName), typeof(string), typeof(SecretField), new PropertyMetadata("表示"));

    public static readonly DependencyProperty HideAutomationNameProperty =
        DependencyProperty.Register(nameof(HideAutomationName), typeof(string), typeof(SecretField), new PropertyMetadata("隠す"));

    public SecretField()
    {
        InitializeComponent();
    }

    /// <summary>Caption shown above the field (e.g. ID, パスワード). Empty hides it.</summary>
    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    /// <summary>The text to display: the real value or a fixed-length mask, decided by the view model.</summary>
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public bool ShowRevealButton
    {
        get => (bool)GetValue(ShowRevealButtonProperty);
        set => SetValue(ShowRevealButtonProperty, value);
    }

    public bool IsRevealed
    {
        get => (bool)GetValue(IsRevealedProperty);
        set => SetValue(IsRevealedProperty, value);
    }

    public ICommand? RevealCommand
    {
        get => (ICommand?)GetValue(RevealCommandProperty);
        set => SetValue(RevealCommandProperty, value);
    }

    public ICommand? CopyCommand
    {
        get => (ICommand?)GetValue(CopyCommandProperty);
        set => SetValue(CopyCommandProperty, value);
    }

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    public string CopyToolTip
    {
        get => (string)GetValue(CopyToolTipProperty);
        set => SetValue(CopyToolTipProperty, value);
    }

    public string CopyAutomationName
    {
        get => (string)GetValue(CopyAutomationNameProperty);
        set => SetValue(CopyAutomationNameProperty, value);
    }

    public string RevealAutomationName
    {
        get => (string)GetValue(RevealAutomationNameProperty);
        set => SetValue(RevealAutomationNameProperty, value);
    }

    public string HideAutomationName
    {
        get => (string)GetValue(HideAutomationNameProperty);
        set => SetValue(HideAutomationNameProperty, value);
    }
}
