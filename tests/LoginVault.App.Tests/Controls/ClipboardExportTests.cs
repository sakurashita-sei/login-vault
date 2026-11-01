using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using LoginVault.App.Controls;
using LoginVault.App.Tests.Support;
using LoginVault.App.ViewModels;
using LoginVault.App.Views;
using LoginVault.Core.Model;

namespace LoginVault.App.Tests.Controls;

/// <summary>
/// Text boxes that show secrets must not export their text through Copy, Cut or drag-and-drop: the guarded copy
/// button (exclusion formats, token, timed clear) is the only route to the clipboard.
/// </summary>
public class ClipboardExportTests
{
    [Fact]
    public void BlockedTextBox_CannotCopyOrCut_AndCancelsDragCopy()
    {
        UiThread.Invoke(() =>
        {
            var box = new TextBox { Text = "secret-架空" };
            ClipboardExport.SetIsBlocked(box, true);
            box.SelectAll();

            Assert.False(ApplicationCommands.Copy.CanExecute(null, box));
            Assert.False(ApplicationCommands.Cut.CanExecute(null, box));
            Assert.True(RaiseDragCopy(box).CommandCancelled);
        });
    }

    [Fact]
    public void OrdinaryTextBox_StillAllowsDragCopy()
    {
        UiThread.Invoke(() =>
        {
            var box = new TextBox { Text = "plain" };
            box.SelectAll();

            Assert.False(RaiseDragCopy(box).CommandCancelled);
        });
    }

    [Fact]
    public void PasswordEntryField_RevealedBox_IsBlocked_AndUsesThePasswordInputScope()
    {
        UiThread.Invoke(() =>
        {
            UiThread.EnsureApplication();
            var field = new PasswordEntryField();
            var plain = Assert.IsType<TextBox>(field.FindName("PlainBox"));

            Assert.True(ClipboardExport.GetIsBlocked(plain));
            Assert.True(RaiseDragCopy(plain).CommandCancelled);
            // IME に「パスワード欄」と伝え、学習・予測の対象から外してもらう（効き方は IME 次第）
            var scope = Assert.Single(plain.InputScope.Names.Cast<InputScopeName>());
            Assert.Equal(InputScopeNameValue.Password, scope.NameValue);
        });
    }

    [Fact]
    public void AccountEditor_LoginIdBox_IsBlocked()
    {
        UiThread.Invoke(() =>
        {
            UiThread.EnsureApplication();
            var service = new ServiceEntry { Id = Guid.NewGuid(), Name = "Google", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
            var document = new VaultDocument { Services = new[] { service } };
            var window = new AccountEditorWindow(new AccountEditorViewModel(document, null, service.Id));
            try
            {
                var box = Descendants(window).OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "ログインID");

                Assert.True(ClipboardExport.GetIsBlocked(box));
                Assert.True(RaiseDragCopy(box).CommandCancelled);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static DataObjectCopyingEventArgs RaiseDragCopy(UIElement element)
    {
        var args = new DataObjectCopyingEventArgs(new DataObject("x"), isDragDrop: true);
        element.RaiseEvent(args);
        return args;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var grandChild in Descendants(child))
            {
                yield return grandChild;
            }
        }
    }

    [Fact]
    public async Task PasswordGenerator_CandidateBox_AllowsCopy_AndSelectsAllOnClick()
    {
        await UiThread.InvokeAsync(async () =>
        {
            UiThread.EnsureApplication();
            var window = new PasswordGeneratorWindow(new PasswordGeneratorViewModel(new PasswordGeneratorOptions()))
            {
                ShowActivated = false,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -32000,
                Top = -32000,
            };
            try
            {
                window.Show();
                await UiThread.WaitUntilAsync(() => window.IsLoaded);
                await UiThread.SettleAsync();

                var box = Assert.IsAssignableFrom<TextBox>(window.FindName("CandidateBox"));

                // 利用者の指示により、生成したパスワードはコピー（Ctrl+C）可能（D-050）
                Assert.False(ClipboardExport.GetIsBlocked(box));
                Assert.True(box.IsReadOnly);

                // クリック（PreviewMouseLeftButtonDown）で全選択される（D-051）
                var mouseDownArgs = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                {
                    RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent,
                    Source = box,
                };
                box.RaiseEvent(mouseDownArgs);
                Assert.True(mouseDownArgs.Handled);
                Assert.Equal(box.Text, box.SelectedText);
                Assert.False(string.IsNullOrEmpty(box.SelectedText));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public async Task PasswordGenerator_SymbolsPanel_IsCollapsedWhenUseSymbolsIsFalse()
    {
        await UiThread.InvokeAsync(async () =>
        {
            UiThread.EnsureApplication();
            var vm = new PasswordGeneratorViewModel(new PasswordGeneratorOptions { UseSymbols = true });
            var window = new PasswordGeneratorWindow(vm)
            {
                ShowActivated = false,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -32000,
                Top = -32000,
            };
            try
            {
                window.Show();
                await UiThread.WaitUntilAsync(() => window.IsLoaded);
                await UiThread.SettleAsync();

                var panel = Assert.IsAssignableFrom<FrameworkElement>(window.FindName("SymbolsPanel"));
                Assert.Equal(Visibility.Visible, panel.Visibility);

                vm.UseSymbols = false;
                await UiThread.SettleAsync();
                Assert.Equal(Visibility.Collapsed, panel.Visibility);

                vm.UseSymbols = true;
                await UiThread.SettleAsync();
                Assert.Equal(Visibility.Visible, panel.Visibility);
            }
            finally
            {
                window.Close();
            }
        });
    }
}
