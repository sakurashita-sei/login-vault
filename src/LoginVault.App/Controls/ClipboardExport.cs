using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace LoginVault.App.Controls;

/// <summary>
/// Attached behavior for text boxes that hold secrets: Copy and Cut are disabled and drag-and-drop copying is
/// cancelled, so the only route to the clipboard is the guarded copy button (exclusion formats, token, timed clear).
/// </summary>
public static class ClipboardExport
{
    public static readonly DependencyProperty IsBlockedProperty = DependencyProperty.RegisterAttached(
        "IsBlocked",
        typeof(bool),
        typeof(ClipboardExport),
        new PropertyMetadata(false, OnIsBlockedChanged));

    public static bool GetIsBlocked(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(IsBlockedProperty);
    }

    public static void SetIsBlocked(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IsBlockedProperty, value);
    }

    private static void OnIsBlockedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // 秘密欄は遮断したら戻さない前提（false への変更は何もしない）
        if (d is not TextBoxBase box || e.NewValue is not true)
        {
            return;
        }

        foreach (var command in new[] { ApplicationCommands.Copy, ApplicationCommands.Cut })
        {
            // インスタンスの CommandBinding は TextEditor のクラス束縛より先に評価される
            box.CommandBindings.Add(new CommandBinding(command, (_, args) => args.Handled = true, (_, args) =>
            {
                args.CanExecute = false;
                args.Handled = true;
            }));
        }

        // ドラッグ＆ドロップによる持ち出し（CommandBinding を通らない）も取り消す
        DataObject.AddCopyingHandler(box, (_, args) => args.CancelCommand());
    }
}
