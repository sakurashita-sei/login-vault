using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LoginVault.App.Startup;
using LoginVault.App.ViewModels;

namespace LoginVault.App.Views;

/// <summary>Main two-pane screen. Code-behind only handles focus shortcuts, double-click and the check marks of the View menu.</summary>
public partial class VaultView : UserControl
{
    public VaultView()
    {
        InitializeComponent();
        PreviewKeyDown += OnPreviewKeyDown;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (Application.Current is App app)
        {
            app.ThemeChanged -= OnThemeChanged;
            app.ThemeChanged += OnThemeChanged;
            RefreshThemeMenu(app.CurrentTheme);
        }

        RefreshCaptureExclusionItem();

        // 状態ドットの点滅は Windows の「アニメーション効果」が有効なときだけ
        if (SystemParameters.ClientAreaAnimation)
        {
            var blink = new System.Windows.Media.Animation.DoubleAnimation(1, 0.25, TimeSpan.FromSeconds(0.9))
            {
                AutoReverse = true,
                RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
            };
            StatusDot.BeginAnimation(OpacityProperty, blink);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (Application.Current is App app)
        {
            app.ThemeChanged -= OnThemeChanged;
        }

        StatusDot.BeginAnimation(OpacityProperty, null);
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        if (sender is App app)
        {
            RefreshThemeMenu(app.CurrentTheme);
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Ctrl+F で検索欄へ移動する（UI 固有処理）
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
    }

    private async void AccountList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // カード内のボタン（コピー・表示・削除）上のダブルクリックは編集として扱わない
        if (IsInsideButton(e.OriginalSource as DependencyObject))
        {
            return;
        }

        if (DataContext is VaultViewModel vault && AccountList.SelectedItem is AccountItemViewModel item)
        {
            await vault.EditAccountAsync(item);
        }
    }

    private static bool IsInsideButton(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is Button)
            {
                return true;
            }

            element = element is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetParent(element)
                : LogicalTreeHelper.GetParent(element);
        }

        return false;
    }

    /// <summary>「表示」メニューのテーマ項目。選択は App が ui-settings.json に保存する（D-033）。</summary>
    private void ThemeItem_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current is not App app)
        {
            return;
        }

        if (sender is MenuItem { Tag: string tag } && Enum.TryParse<ThemeOverride>(tag, ignoreCase: true, out var theme))
        {
            app.SetTheme(theme);
        }
        else
        {
            RefreshThemeMenu(app.CurrentTheme);
        }
    }

    /// <summary>Re-syncs every check mark of the View menu each time it opens (the vault value may have changed, for example by a restore).</summary>
    private void ViewMenu_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (Application.Current is App app)
        {
            RefreshThemeMenu(app.CurrentTheme);
        }

        RefreshCaptureExclusionItem();
    }

    /// <summary>Turns screen-capture exclusion on or off (saved in the vault, D-038) and shows the saved state as the check mark.</summary>
    private async void CaptureExclusionItem_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is VaultViewModel vault)
        {
            await vault.ToggleScreenCaptureExclusionAsync();
        }

        // IsCheckable の項目はクリックで自分のチェックを先に反転させるため、保存の結果（保管庫の値）で必ず上書きする。保存に失敗したら元に戻る
        RefreshCaptureExclusionItem();
    }

    private void RefreshCaptureExclusionItem() =>
        CaptureExclusionItem.IsChecked = DataContext is VaultViewModel { IsScreenCaptureExcluded: true };

    /// <summary>Checks exactly the item of the current theme (IsCheckable items toggle themselves on click).</summary>
    private void RefreshThemeMenu(ThemeOverride current)
    {
        foreach (var item in new[] { ThemeSystemItem, ThemeLightItem, ThemeDarkItem, ThemeSfItem })
        {
            item.IsChecked = item.Tag is string tag && Enum.TryParse<ThemeOverride>(tag, ignoreCase: true, out var theme) && theme == current;
        }
    }
}
