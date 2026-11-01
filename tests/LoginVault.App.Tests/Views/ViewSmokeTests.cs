using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LoginVault.App.Clipboard;
using LoginVault.App.Locking;
using LoginVault.App.Tests.Support;
using LoginVault.App.Themes;
using LoginVault.App.ViewModels;
using LoginVault.App.Views;
using LoginVault.Core.Model;
using LoginVault.Core.Operations;
using LoginVault.Infrastructure.Session;
using LoginVault.Infrastructure.Storage;

namespace LoginVault.App.Tests.Views;

/// <summary>
/// Instantiates every view and dialog with realistic view models on an STA thread, forces layout so
/// data templates are realized, and fails on XAML parse errors or data-binding errors. This catches
/// wrong resource keys and binding paths without showing any window. Runs once per theme (light, dark,
/// SF) and checks that the three palette dictionaries define the same keys.
/// </summary>
public class ViewSmokeTests : IDisposable
{
    private const string Password = "テスト用マスターパスワード 架空 2026";
    private readonly TempDataDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public async Task AllViewsAndDialogs_LoadWithoutXamlOrBindingErrors()
    {
        // 非同期の準備は UI スレッドの外で済ませる（STA 上で同期待ちするとデッドロックするため）
        var time = new FakeTime();
        var lifecycle = new VaultLifecycle(new VaultFileStore(new PhysicalFileSystem(), new VaultPaths(_dir.Path), time), time);
        var shell = new ShellViewModel(lifecycle, new FakeDialogService(), new ClipboardGuard(new FakeClipboardDevice(), time, _ => Task.CompletedTask), new LockController(time, time), time, time);
        await shell.InitializeVaultAsync(Password, Password);
        var vault = shell.Vault!;
        var token = vault.Snapshot.SessionToken;
        var google = await lifecycle.CommitAsync(token, d => VaultOperations.AddService(d, "Google", time.UtcNow));
        var amazon = await lifecycle.CommitAsync(token, d => VaultOperations.AddService(d, "Amazon", time.UtcNow));
        var main = await lifecycle.CommitAsync(token, d => VaultOperations.AddAccount(d, new AccountInput { ServiceId = google.CreatedId!.Value, Label = "メイン", LoginIdText = "main@example.com", Password = PasswordChange.Set("pw1"), IsEmailAccount = true, MfaStatus = MfaStatus.Enabled, MfaMethods = new[] { new MfaMethod(MfaMethodType.Sms, Detail: "末尾1234") } }, time.UtcNow));
        await lifecycle.CommitAsync(token, d => VaultOperations.AddAccount(d, new AccountInput { ServiceId = amazon.CreatedId!.Value, Label = "個人用", LoginIdMode = LoginIdMode.EmailReference, LoginEmailAccountId = main.CreatedId!.Value, Password = PasswordChange.Set("pw2"), Notes = "メモ" }, time.UtcNow));
        vault.RefreshFromLifecycle();
        var document = vault.Snapshot.Document;
        var mainAccount = document.Accounts.Single(a => a.Id == main.CreatedId!.Value);
        var referrers = VaultQueries.ReferrersOf(document, mainAccount.Id);

        var bindingErrors = new List<string>();
        var realizedAccountRows = 0;
        UiThread.Invoke(() => realizedAccountRows = RunOnStaThread(shell, vault, document, mainAccount, referrers, bindingErrors));

        Assert.Empty(bindingErrors);
        Assert.True(realizedAccountRows >= 1, "アカウント一覧の行テンプレートが実体化されていません。");
    }

    private static int RunOnStaThread(ShellViewModel shell, VaultViewModel vault, VaultDocument document, AccountEntry mainAccount, IReadOnlyList<AccountEntry> referrers, List<string> bindingErrors)
    {
        // App.xaml のリソース（コンバーター・スタイル）を読み込む。OnStartup は呼ばれない
        UiThread.EnsureApplication();
        var application = Application.Current!;
        var listener = new CollectingListener(bindingErrors);
        PresentationTraceSources.Refresh();
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        try
        {
            var rows = 0;
            var keySets = new Dictionary<FluentThemeMode, HashSet<string>>();
            foreach (var mode in new[] { FluentThemeMode.Light, FluentThemeMode.Dark, FluentThemeMode.Sf })
            {
                // 配色辞書を差し替えても全画面が生成でき、束縛エラーが出ないことをテーマごとに確認する
                FluentTheme.Apply(application, mode, null);
                rows = LayoutEverything(shell, vault, document, mainAccount, referrers);
                keySets[mode] = PaletteKeys(application);
            }

            FluentTheme.Apply(application, FluentThemeMode.Light, null);
            foreach (var (mode, keys) in keySets)
            {
                var differences = keySets[FluentThemeMode.Light].Except(keys).Concat(keys.Except(keySets[FluentThemeMode.Light])).OrderBy(k => k, StringComparer.Ordinal).ToList();
                Assert.True(differences.Count == 0, $"{mode} の配色辞書のキーがライトと一致しません: {string.Join(", ", differences)}");
            }

            return rows;
        }
        finally
        {
            PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
        }
    }

    /// <summary>The keys of the palette dictionary currently merged into the application resources.</summary>
    private static HashSet<string> PaletteKeys(Application application)
    {
        var palette = application.Resources.MergedDictionaries.Single(d => d.Source is { } source
            && (source.OriginalString.Contains("Fluent.Light", StringComparison.Ordinal) || source.OriginalString.Contains("Fluent.Dark", StringComparison.Ordinal) || source.OriginalString.Contains("Fluent.Sf", StringComparison.Ordinal)));
        return palette.Keys.Cast<object>().Select(k => k.ToString()!).ToHashSet(StringComparer.Ordinal);
    }

    private static int LayoutEverything(ShellViewModel shell, VaultViewModel vault, VaultDocument document, AccountEntry mainAccount, IReadOnlyList<AccountEntry> referrers)
    {
        {
            var mainWindow = new MainWindow { DataContext = shell };
            Layout((UIElement)mainWindow.Content);
            Assert.Equal(shell.WindowTitle, mainWindow.Title);
            mainWindow.Close();

            Layout(new SetupView { DataContext = shell });
            Layout(new UnlockView { DataContext = shell });

            var vaultView = new VaultView { DataContext = vault };
            vault.SelectedServiceItem = vault.ServiceItems.First(s => s.Kind == ServiceListItemKind.All);
            Layout(vaultView);
            var rows = CountVisuals<ListBoxItem>(vaultView);

            var dialogs = new Window[]
            {
                new AccountEditorWindow(new AccountEditorViewModel(document, null, document.Services[0].Id)),
                new AccountEditorWindow(new AccountEditorViewModel(document, mainAccount, null)),
                new EmailChangeImpactWindow(new EmailChangeImpactViewModel(document, "main@example.com", "renamed@example.com", referrers)),
                new SettingsWindow(new SettingsViewModel(document.Preferences, @"C:\dummy\LoginVault")),
                new PasswordGeneratorWindow(new PasswordGeneratorViewModel(document.Preferences.PasswordGenerator)),
                new ChangeMasterPasswordWindow(),
                new RestoreWindow(),
                new TextPromptWindow("タイトル", "メッセージ", "初期値"),
                new ConfirmWindow("タイトル", "メッセージ", new[] { "詳細1", "詳細2" }, "実行", isDestructive: true, showCancel: true),
                new SplashWindow(SplashModel()),
            };
            foreach (var dialog in dialogs)
            {
                Layout((UIElement)dialog.Content);
                dialog.Close();
            }

            return rows;
        }
    }

    private static SplashViewModel SplashModel()
    {
        var model = new SplashViewModel("0.1.0", ".NET 8");
        model.StartStep(2);
        return model;
    }

    private static void Layout(UIElement element)
    {
        // 標準サイズと最小ウィンドウサイズ（760×480）の両方でレイアウトする
        foreach (var size in new[] { new Size(1000, 700), new Size(760, 480) })
        {
            element.Measure(size);
            element.Arrange(new Rect(size));
            element.UpdateLayout();
        }
    }

    /// <summary>
    /// Every {StaticResource}/{DynamicResource} key used in the app's XAML must exist in the application resources
    /// (merged palette + control dictionaries) or be defined in the same file. DynamicResource misses are silent at
    /// runtime, so this is the only place they are caught.
    /// </summary>
    [Fact]
    public void ResourceKeysReferencedInXaml_AreAllDefined()
    {
        var root = FindRepositoryRoot();
        var appDirectory = Path.Combine(root, "src", "LoginVault.App");
        var files = Directory.GetFiles(appDirectory, "*.xaml", SearchOption.AllDirectories).Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(files);
        var defined = new HashSet<string>(StringComparer.Ordinal);
        var referencesByFile = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var keyPattern = new System.Text.RegularExpressions.Regex("x:Key=\"([^\"{}]+)\"");
        var referencePattern = new System.Text.RegularExpressions.Regex("\\{(?:StaticResource|DynamicResource)\\s+([A-Za-z0-9_.]+)\\s*\\}");
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (System.Text.RegularExpressions.Match m in keyPattern.Matches(text))
            {
                defined.Add(m.Groups[1].Value);
            }

            referencesByFile[file] = referencePattern.Matches(text).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        }

        Assert.True(referencesByFile.Values.Sum(v => v.Count) > 50, "XAML からのリソース参照が抽出できていません。");
        var missing = referencesByFile
            .SelectMany(kv => kv.Value.Where(key => !defined.Contains(key)).Select(key => $"{Path.GetFileName(kv.Key)}: {key}"))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();
        Assert.True(missing.Count == 0, "定義のないリソースキーを参照しています: " + string.Join(", ", missing));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "LoginVault.sln")))
        {
            directory = directory.Parent;
        }

        Assert.True(directory is not null, "LoginVault.sln が見つかりません（テストはリポジトリ内で実行してください）。");
        return directory!.FullName;
    }

    private static int CountVisuals<T>(DependencyObject root)
        where T : DependencyObject
    {
        var count = 0;
        var children = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < children; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T)
            {
                count++;
            }

            count += CountVisuals<T>(child);
        }

        return count;
    }

    private sealed class CollectingListener : TraceListener
    {
        private readonly List<string> _errors;

        public CollectingListener(List<string> errors)
        {
            _errors = errors;
        }

        public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? format, params object?[]? args)
        {
            if (eventType is TraceEventType.Error or TraceEventType.Critical)
            {
                var message = args is { Length: > 0 } && format is not null ? string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args) : format ?? string.Empty;
                _errors.Add($"{eventType} {id}: {message}");
            }
        }

        public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? message)
        {
            if (eventType is TraceEventType.Error or TraceEventType.Critical)
            {
                _errors.Add($"{eventType} {id}: {message}");
            }
        }

        public override void Write(string? message)
        {
        }

        public override void WriteLine(string? message)
        {
        }
    }
}
