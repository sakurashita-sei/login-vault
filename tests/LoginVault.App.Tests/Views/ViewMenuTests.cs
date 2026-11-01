using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using LoginVault.App.Clipboard;
using LoginVault.App.Locking;
using LoginVault.App.Tests.Support;
using LoginVault.App.ViewModels;
using LoginVault.App.Views;
using LoginVault.Infrastructure.Session;
using LoginVault.Infrastructure.Storage;

namespace LoginVault.App.Tests.Views;

/// <summary>
/// The "表示" menu item for screen-capture exclusion must show its state as a check mark: it is checkable (the
/// template reserves the check column only for checkable items) and the check always follows the value saved in
/// the vault, even though WPF flips a checkable item by itself on click before the save finishes or fails.
/// </summary>
public class ViewMenuTests : IDisposable
{
    private const string Password = "テスト用マスターパスワード 架空 2026";
    private readonly TempDataDir _dir = new();
    private readonly ReplaceSwitchFileSystem _fs = new();

    public void Dispose() => _dir.Dispose();

    private async Task<VaultViewModel> CreateVaultAsync()
    {
        var time = new FakeTime();
        var lifecycle = new VaultLifecycle(new VaultFileStore(_fs, new VaultPaths(_dir.Path), time), time);
        var shell = new ShellViewModel(lifecycle, new FakeDialogService(), new ClipboardGuard(new FakeClipboardDevice(), time, _ => Task.CompletedTask), new LockController(time, time), time, time);
        await shell.InitializeVaultAsync(Password, Password);
        return shell.Vault!;
    }

    private static void OpenSubmenu(MenuItem menu) => menu.RaiseEvent(new RoutedEventArgs(MenuItem.SubmenuOpenedEvent, menu));

    /// <summary>Clicks like a user or screen reader would (MenuItem toggles a checkable item, then raises Click after render).</summary>
    private static void Click(MenuItem item) => ((IInvokeProvider)new MenuItemAutomationPeer(item).GetPattern(PatternInterface.Invoke)!).Invoke();

    [Fact]
    public async Task CaptureExclusionItem_IsCheckable_AndShowsTheSavedStateWhenTheMenuOpens()
    {
        var vault = await CreateVaultAsync();
        await vault.ToggleScreenCaptureExclusionAsync();
        Assert.True(vault.IsScreenCaptureExcluded);

        await UiThread.InvokeAsync(() =>
        {
            UiThread.EnsureApplication();
            var view = new VaultView { DataContext = vault };
            var menu = Assert.IsType<MenuItem>(view.FindName("ViewMenu"));
            var item = Assert.IsType<MenuItem>(view.FindName("CaptureExclusionItem"));

            Assert.True(item.IsCheckable);
            item.IsChecked = false;
            OpenSubmenu(menu);
            Assert.True(item.IsChecked);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task CaptureExclusionItem_Click_SavesAndChecks_AndRestoresTheCheckWhenSavingFails()
    {
        var vault = await CreateVaultAsync();

        await UiThread.InvokeAsync(async () =>
        {
            UiThread.EnsureApplication();
            var view = new VaultView { DataContext = vault };
            var menu = Assert.IsType<MenuItem>(view.FindName("ViewMenu"));
            var item = Assert.IsType<MenuItem>(view.FindName("CaptureExclusionItem"));
            OpenSubmenu(menu);
            Assert.False(item.IsChecked);

            // オンにする: 保存されてチェックが付く
            Click(item);
            await UiThread.WaitUntilAsync(() => vault.IsScreenCaptureExcluded && item.IsChecked);

            // オフにしようとして保存に失敗: WPF はクリックでチェックを外すが、保管庫の値（オン）に戻る
            _fs.FailReplace = true;
            Click(item);
            await UiThread.WaitUntilAsync(() => vault.ErrorMessage is not null && item.IsChecked);
            Assert.True(vault.IsScreenCaptureExcluded);
            Assert.Contains("保存に失敗", vault.ErrorMessage, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task AccountMenu_OffersOnlyAddEditAndDelete()
    {
        // ID・パスワードのコピーと表示切替は一覧のカードのボタンで行う（メニューには置かない。D-046）
        var vault = await CreateVaultAsync();

        await UiThread.InvokeAsync(() =>
        {
            UiThread.EnsureApplication();
            var view = new VaultView { DataContext = vault };
            var menu = LogicalDescendants(view).OfType<MenuItem>().Single(m => Equals(m.Header, "アカウント(_A)"));
            var entries = menu.Items.Cast<object>().Select(i => i is MenuItem item ? item.Header as string : "(separator)").ToArray();

            Assert.Equal(new[] { "追加(_A)...", "編集(_E)...", "削除(_D)..." }, entries);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task ViewMenu_GroupsTheThemesInASubmenu_FollowedByCaptureExclusion()
    {
        // 表示 > テーマ > テーマ一覧（D-047）
        var vault = await CreateVaultAsync();

        await UiThread.InvokeAsync(() =>
        {
            UiThread.EnsureApplication();
            var view = new VaultView { DataContext = vault };
            var viewMenu = Assert.IsType<MenuItem>(view.FindName("ViewMenu"));
            var entries = viewMenu.Items.Cast<object>().Select(i => i is MenuItem item ? item.Header as string : "(separator)").ToArray();
            Assert.Equal(new[] { "テーマ(_T)", "画面キャプチャから除外(_C)" }, entries);

            var themeMenu = Assert.IsType<MenuItem>(viewMenu.Items[0]);
            var themes = themeMenu.Items.Cast<MenuItem>().ToArray();
            Assert.Equal(new[] { "システム設定に従う(_Y)", "ライト(_L)", "ダーク(_D)", "SF(_F)" }, themes.Select(t => t.Header as string).ToArray());
            Assert.All(themes, t => Assert.True(t.IsCheckable));
            Assert.Same(view.FindName("ThemeLightItem"), themes[1]);
            return Task.CompletedTask;
        });
    }

    private static IEnumerable<DependencyObject> LogicalDescendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var grandChild in LogicalDescendants(child))
            {
                yield return grandChild;
            }
        }
    }

    /// <summary>Real file system whose replace (every save after the first) can be made to fail.</summary>
    private sealed class ReplaceSwitchFileSystem : IVaultFileSystem
    {
        private readonly PhysicalFileSystem _inner = new();

        public bool FailReplace { get; set; }

        public bool FileExists(string path) => _inner.FileExists(path);

        public long GetFileLength(string path) => _inner.GetFileLength(path);

        public byte[] ReadAllBytes(string path) => _inner.ReadAllBytes(path);

        public void WriteNewFileDurably(string path, ReadOnlySpan<byte> content) => _inner.WriteNewFileDurably(path, content);

        public void ReplaceFile(string sourcePath, string destinationPath, string backupPath)
        {
            if (FailReplace)
            {
                throw new IOException("テスト用の置き換え失敗");
            }

            _inner.ReplaceFile(sourcePath, destinationPath, backupPath);
        }

        public void MoveFile(string sourcePath, string destinationPath) => _inner.MoveFile(sourcePath, destinationPath);

        public void CopyFile(string sourcePath, string destinationPath, bool overwrite) => _inner.CopyFile(sourcePath, destinationPath, overwrite);

        public void DeleteFile(string path) => _inner.DeleteFile(path);

        public void CreateDirectory(string path) => _inner.CreateDirectory(path);

        public IReadOnlyList<string> ListFiles(string directory, string searchPattern) => _inner.ListFiles(directory, searchPattern);
    }
}
