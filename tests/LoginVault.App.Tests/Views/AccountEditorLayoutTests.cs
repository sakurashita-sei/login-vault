using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using LoginVault.App.Controls;
using LoginVault.App.Tests.Support;
using LoginVault.App.ViewModels;
using LoginVault.App.Views;
using LoginVault.Core.Model;

namespace LoginVault.App.Tests.Views;

/// <summary>
/// The account editor shows only service, ID and password; everything else sits in a "詳細設定" section that is
/// closed by default and opened with a toggle (D-045).
/// </summary>
public class AccountEditorLayoutTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 9, 0, 0, TimeSpan.FromHours(9));

    private static VaultDocument Document(out ServiceEntry service, out AccountEntry emailAccount)
    {
        service = new ServiceEntry { Id = Guid.NewGuid(), Name = "Google", CreatedAt = Now, UpdatedAt = Now };
        emailAccount = new AccountEntry { Id = Guid.NewGuid(), ServiceId = service.Id, Label = "メイン", LoginIdMode = LoginIdMode.Direct, LoginIdText = "main@example.com", Password = "pw-架空", IsEmailAccount = true, Notes = "架空のメモ", CreatedAt = Now, UpdatedAt = Now };
        return new VaultDocument { Services = new[] { service }, Accounts = new[] { emailAccount } };
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

    private static bool IsInside(DependencyObject element, DependencyObject container)
    {
        for (var current = LogicalTreeHelper.GetParent(element); current is not null; current = LogicalTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, container))
            {
                return true;
            }
        }

        return false;
    }

    private static T Named<T>(Window window, string automationName)
        where T : DependencyObject =>
        Descendants(window).OfType<T>().Single(e => AutomationProperties.GetName(e) == automationName);

    [Fact]
    public async Task BasicFields_AreAlwaysShown_AndTheRestIsInsideACollapsedDetailsSection()
    {
        await UiThread.InvokeAsync(async () =>
        {
            UiThread.EnsureApplication();
            var document = Document(out var service, out _);
            var editor = new AccountEditorViewModel(document, null, service.Id);
            var window = new AccountEditorWindow(editor);
            try
            {
                await UiThread.SettleAsync();
                var details = Assert.IsAssignableFrom<FrameworkElement>(window.FindName("DetailsPanel"));
                var toggle = Assert.IsAssignableFrom<ToggleButton>(window.FindName("DetailsToggle"));
                Assert.Equal(Visibility.Collapsed, details.Visibility);
                Assert.Equal("詳細設定", AutomationProperties.GetName(toggle));

                // 基本欄（サービス・ID・メール参照トグル・パスワード）は詳細設定の外（D-049）
                Assert.False(IsInside(Named<ComboBox>(window, "サービス名"), details));
                Assert.False(IsInside(Named<TextBox>(window, "ログインID"), details));
                Assert.False(IsInside(Named<ComboBox>(window, "参照するメールアカウント"), details));
                Assert.False(IsInside(Named<CheckBox>(window, "登録済みメールから選択"), details));
                Assert.False(IsInside(Assert.IsType<PasswordEntryField>(window.FindName("NewPasswordField")), details));

                // それ以外（表示名・メールアカウントの質問・二段階認証・メモ）は詳細設定の中
                Assert.True(IsInside(Named<TextBox>(window, "アカウント表示名"), details));
                Assert.True(IsInside(Named<TextBox>(window, "メモ"), details));
                Assert.True(IsInside(Named<TextBox>(window, "SMS の補足"), details));
                Assert.True(IsInside(Descendants(window).OfType<RadioButton>().Single(r => Equals(r.Content, "未設定")), details));
                Assert.True(IsInside(Descendants(window).OfType<RadioButton>().Single(r => Equals(r.Content, "設定あり")), details));
                Assert.DoesNotContain(Descendants(window).OfType<RadioButton>(), r => Equals(r.Content, "未確認"));

                editor.IsDetailsExpanded = true;
                await UiThread.SettleAsync();
                Assert.Equal(Visibility.Visible, details.Visibility);
                Assert.True(toggle.IsChecked);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public async Task EditingAnAccountWithDetails_ShowsTheHintInTheToggleWhileCollapsed()
    {
        await UiThread.InvokeAsync(async () =>
        {
            UiThread.EnsureApplication();
            var document = Document(out _, out var emailAccount);
            var editor = new AccountEditorViewModel(document, emailAccount, null);
            var window = new AccountEditorWindow(editor);
            try
            {
                await UiThread.SettleAsync();
                var toggle = Assert.IsAssignableFrom<ToggleButton>(window.FindName("DetailsToggle"));
                Assert.False(toggle.IsChecked);
                Assert.Contains(Descendants(toggle).OfType<TextBlock>(), t => t.Text == "詳細設定（設定あり）");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Window_SizesToItsContent_ButNeverGrowsTallerThanTheCap()
    {
        UiThread.Invoke(() =>
        {
            UiThread.EnsureApplication();
            var document = Document(out var service, out _);
            var window = new AccountEditorWindow(new AccountEditorViewModel(document, null, service.Id));
            try
            {
                // 閉じた状態は内容の高さ、詳細設定を開いても上限で止まり、超えた分は画面内でスクロールする
                Assert.Equal(SizeToContent.Height, window.SizeToContent);
                Assert.Equal(AccountEditorWindow.MaxContentHeight, window.MaxHeight);
                Assert.InRange(AccountEditorWindow.MaxContentHeight, 480, 680);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public async Task NewServiceName_AppearsBelowTheServiceList_OnlyWhenTheNewServiceEntryIsChosen()
    {
        await UiThread.InvokeAsync(async () =>
        {
            UiThread.EnsureApplication();
            var document = Document(out var service, out _);
            var editor = new AccountEditorViewModel(document, null, service.Id);
            var window = new AccountEditorWindow(editor);
            try
            {
                await UiThread.SettleAsync();
                var combo = Named<ComboBox>(window, "サービス名");
                var box = Named<TextBox>(window, "新しいサービス名");
                var details = Assert.IsAssignableFrom<FrameworkElement>(window.FindName("DetailsPanel"));
                Assert.Contains(AccountEditorViewModel.NewServiceChoice, combo.Items.Cast<object>());
                Assert.False(IsInside(box, details));
                Assert.Equal(Visibility.Collapsed, box.Visibility);

                editor.SelectedService = editor.ServiceOptions[^1];
                await UiThread.SettleAsync();
                Assert.Equal(Visibility.Visible, box.Visibility);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public async Task EditorOpenedWithANewService_PutsTheFocusInTheServiceNameBox()
    {
        await UiThread.InvokeAsync(async () =>
        {
            UiThread.EnsureApplication();
            // サービスが 1 件もない保管庫: 新しいサービスの入力で開き、名前の欄から打ち始められる
            var editor = new AccountEditorViewModel(new VaultDocument(), null, null);
            var window = new AccountEditorWindow(editor)
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
                Assert.Same(window.FindName("NewServiceBox"), FocusManager.GetFocusedElement(window));
            }
            finally
            {
                window.Close();
            }
        });
    }
}
