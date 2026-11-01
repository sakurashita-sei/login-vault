using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Threading;

namespace LoginVault.App.Tests.Support;

/// <summary>
/// One STA thread with a running dispatcher, shared by every test that touches WPF. The Application (and its
/// resource dictionaries) and all controls are thread-affine, so tests must not each spin up their own STA thread.
/// </summary>
internal static class UiThread
{
    private static readonly Lazy<Dispatcher> Instance = new(Start, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Runs <paramref name="action"/> on the UI thread and rethrows its exception (with the original stack) on the caller.</summary>
    public static void Invoke(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Exception? failure = null;
        Instance.Value.Invoke(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        if (failure is not null)
        {
            ExceptionDispatchInfo.Throw(failure);
        }
    }

    /// <summary>Runs an async function on the UI thread (its awaits resume there, so dispatcher work such as deferred clicks runs in between) and completes when it finishes.</summary>
    public static Task InvokeAsync(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return Instance.Value.InvokeAsync(action).Task.Unwrap();
    }

    /// <summary>Waits on the UI thread until <paramref name="condition"/> holds, yielding to the dispatcher between checks.</summary>
    public static async Task WaitUntilAsync(Func<bool> condition, int timeoutMilliseconds = 5000)
    {
        ArgumentNullException.ThrowIfNull(condition);
        var deadline = Environment.TickCount64 + timeoutMilliseconds;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
            {
                throw new TimeoutException("UI の状態が期待どおりになりませんでした。");
            }

            await Task.Delay(20);
        }
    }

    /// <summary>
    /// Inside <see cref="InvokeAsync"/>: lets the dispatcher finish queued work of higher priority than Background (data
    /// binding, layout, Loaded) and resumes afterwards. It yields instead of pumping a nested loop, so other tests' UI work
    /// never runs inside this one.
    /// </summary>
    public static async Task SettleAsync() => await Dispatcher.Yield(DispatcherPriority.Background);

    /// <summary>Loads App.xaml resources (converters, styles, palette) once. OnStartup is never called. Call on the UI thread.</summary>
    public static void EnsureApplication()
    {
        if (Application.Current is null)
        {
            var app = new App();
            app.InitializeComponent();
        }
    }

    private static Dispatcher Start()
    {
        Dispatcher? dispatcher = null;
        using var ready = new ManualResetEventSlim(false);
        var thread = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "LoginVault UI test thread",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        return dispatcher!;
    }
}
