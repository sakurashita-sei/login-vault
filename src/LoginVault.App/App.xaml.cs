using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using LoginVault.App.Clipboard;
using LoginVault.App.Locking;
using LoginVault.App.Services;
using LoginVault.App.Startup;
using LoginVault.App.Themes;
using LoginVault.App.ViewModels;
using LoginVault.App.Views;
using LoginVault.Core.Time;
using LoginVault.Infrastructure.Session;
using LoginVault.Infrastructure.Storage;

namespace LoginVault.App;

/// <summary>Application entry and the only composition root: dependencies are created here and passed by constructor.</summary>
public partial class App : Application
{
    private ThemeOverride _themeOverride = ThemeOverride.System;
    private string? _dataDirectory;
    private SplashWindow? _splash;
    private SingleInstanceGuard? _instanceGuard;
    private VaultLifecycle? _lifecycle;
    private ClipboardGuard? _clipboard;
    private ShellViewModel? _shell;
    private SystemEventsLockSignals? _signals;
    private InputActivityMonitor? _activity;

    /// <summary>Minimum time each startup step stays visible on the splash so the progress can be read.</summary>
    private static readonly TimeSpan SplashStepDelay = TimeSpan.FromMilliseconds(350);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        _ = StartGuardedAsync(e.Args);
    }

    /// <summary>Startup failures must end the process with a message instead of leaving a splash (or nothing) behind.</summary>
    private async Task StartGuardedAsync(string[] args)
    {
        try
        {
            await StartAsync(args);
        }
        catch (Exception ex)
        {
            _splash?.Close();
            // 起動時の例外に秘密情報は含まれない（保管庫は未解錠）。種別と本文だけを表示する
            MessageBox.Show("起動に失敗しました。" + Environment.NewLine + ex.GetType().Name + ": " + ex.Message, "Login Vault", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    /// <summary>Runs the real startup steps behind the splash screen, then shows the main window.</summary>
    private async Task StartAsync(string[] args)
    {
        // 手順 1: 起動引数とデータディレクトリ（スプラッシュ表示前。テーマを決めるために先に読む）
        var options = CommandLineOptions.Parse(args);
        if (!options.IsValid)
        {
            MessageBox.Show(
                string.Join(Environment.NewLine, options.Errors) + Environment.NewLine + Environment.NewLine + "使い方: LoginVault.exe [--data-dir <データディレクトリ>] [--theme system|light|dark|sf]",
                "Login Vault",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(2);
            return;
        }

        var dataDirectory = Path.GetFullPath(DataDirectoryResolver.ResolveFromEnvironment(options.DataDirectory));
        _dataDirectory = dataDirectory;
        // --theme を指定した起動ではその値（system を含む）を使い、保存はしない。未指定なら保存済みの選択（なければ Windows に追従）
        _themeOverride = ThemeSettings.Resolve(options.Theme, ThemeSettings.Load(dataDirectory));
        ApplySystemTheme();
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        var splashModel = new SplashViewModel(AppVersion.Product, ".NET " + Environment.Version.Major.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var splash = new SplashWindow(splashModel);
        _splash = splash;
        splashModel.StartStep(0);
        splash.Show();
        await Task.Delay(SplashStepDelay);

        // 手順 2: 多重起動の確認
        splashModel.StartStep(1);
        _instanceGuard = new SingleInstanceGuard(dataDirectory);
        if (!_instanceGuard.IsPrimaryInstance)
        {
            splash.Close();
            MessageBox.Show("Login Vault は既に起動しています（同じデータディレクトリを開いています）。", "Login Vault", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown(0);
            return;
        }

        await Task.Delay(SplashStepDelay);

        // 手順 3: 保管庫の確認（ファイルの有無。復号はしない）
        splashModel.StartStep(2);
        var clock = SystemClock.Instance;
        var scheduler = new DispatcherTimerScheduler(Dispatcher);
        _lifecycle = new VaultLifecycle(new VaultFileStore(new PhysicalFileSystem(), new VaultPaths(dataDirectory), clock), clock);
        _ = _lifecycle.VaultExists;
        await Task.Delay(SplashStepDelay);

        // 手順 4: 画面の準備
        splashModel.StartStep(3);
        var window = new MainWindow();
        // HWND は UI スレッドで一度だけ取得して保持する。クリップボードの再試行や終了時の消去は別スレッドで走るため、
        // そこから WPF の Window に触れてはならない（VerifyAccess で例外になる）
        var ownerHandle = IntPtr.Zero;
        window.SourceInitialized += (_, _) => ownerHandle = new WindowInteropHelper(window).Handle;
        // 自分のコピーの消去は、HWND が生きているウィンドウを閉じる時点で行う（OnExit では HWND が破棄済み）。
        // 閉じた後はハンドルを捨て、以後の試行は所有者なしでクリップボードを開く
        window.Closing += (_, _) => TryClearOwnClipboardContent();
        window.Closed += (_, _) => ownerHandle = IntPtr.Zero;
        _clipboard = new ClipboardGuard(new Win32ClipboardDevice(() => ownerHandle), scheduler);
        var lockController = new LockController(clock, scheduler);
        var dialogs = new WpfDialogService(() => window, lockController.NotifyActivity);
        _shell = new ShellViewModel(_lifecycle, dialogs, _clipboard, lockController, scheduler, clock);
        // 画面キャプチャ除外は保管庫の中の設定（解錠後に分かる）。Shell の値に追従して全ウィンドウへ適用する
        _shell.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ShellViewModel.IsScreenCaptureExcluded))
            {
                ScreenCaptureExclusion.Apply(this, _shell.IsScreenCaptureExcluded);
            }
        };

        window.DataContext = _shell;
        window.Deactivated += (_, _) => _shell.NotifyDeactivated();
        _signals = new SystemEventsLockSignals(lockController, Dispatcher);
        _activity = new InputActivityMonitor(lockController, InputManager.Current);
        SessionEnding += (_, _) => _ = _shell.LockAsync(LockReason.Exit);
        await Task.Delay(SplashStepDelay);
        splashModel.FinishAll();
        await Task.Delay(SplashStepDelay);

        MainWindow = window;
        splash.Close();
        _splash = null;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        window.Show();
    }

    private void TryClearOwnClipboardContent()
    {
        // 最初の試行は呼び出しスレッドで同期的に行われ、Busy 時の再試行だけがスレッドプールに移る
        try
        {
            _clipboard?.ClearNowIfOwnAsync().Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // 消去失敗は致命的ではない（強制終了時の消去は保証しない設計）
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // 終了時は自分のコピーが残っていればクリップボードを消去し、鍵を破棄する。
        // 消去の最初の試行は UI スレッド上で同期的に行われ、Busy 時の再試行だけがスレッドプールに移る（ClipboardGuard は UI 文脈に依存しない）
        try
        {
            _clipboard?.ClearNowIfOwnAsync().Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // 消去失敗は致命的ではない（強制終了時の消去は保証しない設計）
        }

        var lifecycle = _lifecycle;
        if (lifecycle is not null)
        {
            try
            {
                Task.Run(() => lifecycle.LockAsync()).Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException)
            {
                // 終了処理の失敗は無視する（保管庫ファイルは既に整合した状態で保存済み）
            }
        }

        Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _activity?.Dispose();
        _signals?.Dispose();
        _lifecycle?.Dispose();
        _instanceGuard?.Dispose();
        base.OnExit(e);
    }

    /// <summary>Raised after the theme has been (re)applied: startup, in-app selection or a Windows setting change.</summary>
    public event EventHandler? ThemeChanged;

    /// <summary>The theme chosen by --theme or in the app. System follows the Windows app mode.</summary>
    public ThemeOverride CurrentTheme => _themeOverride;

    /// <summary>Switches the theme and remembers the choice in the data directory (D-033).</summary>
    public void SetTheme(ThemeOverride theme)
    {
        _themeOverride = theme;
        ApplySystemTheme();
        if (_dataDirectory is null)
        {
            return;
        }

        try
        {
            ThemeSettings.Save(_dataDirectory, theme);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            // 保存できなくてもこのセッションの表示は切り替わっている。次回起動時は保存済みの値（または既定）に戻る
        }
    }

    /// <summary>Follows the Windows app mode (light/dark) and accent color unless a theme is forced.</summary>
    private void ApplySystemTheme()
    {
        var mode = FluentTheme.ResolveMode(_themeOverride, FluentTheme.DetectSystemMode());
        FluentTheme.Apply(this, mode, FluentTheme.DetectSystemAccent());
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnUserPreferenceChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if (e.Category is Microsoft.Win32.UserPreferenceCategory.General or Microsoft.Win32.UserPreferenceCategory.Color or Microsoft.Win32.UserPreferenceCategory.VisualStyle)
        {
            Dispatcher.BeginInvoke(ApplySystemTheme);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // 例外メッセージに秘密情報は含めない設計。種別と本文だけを表示して継続する
        MessageBox.Show(
            "予期しないエラーが発生しました。" + Environment.NewLine + e.Exception.GetType().Name + ": " + e.Exception.Message,
            "Login Vault",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}
