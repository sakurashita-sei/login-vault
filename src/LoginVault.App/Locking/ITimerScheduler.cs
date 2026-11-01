using System.Windows.Threading;

namespace LoginVault.App.Locking;

/// <summary>One-shot timers, abstracted so tests can drive time manually.</summary>
public interface ITimerScheduler
{
    /// <summary>Runs <paramref name="callback"/> once after <paramref name="delay"/>. Disposing the handle cancels it.</summary>
    IDisposable Schedule(TimeSpan delay, Action callback);
}

/// <summary>Timers that fire on the WPF dispatcher thread.</summary>
public sealed class DispatcherTimerScheduler : ITimerScheduler
{
    private readonly Dispatcher _dispatcher;

    public DispatcherTimerScheduler(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public IDisposable Schedule(TimeSpan delay, Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var timer = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher)
        {
            Interval = delay <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : delay,
        };
        var handle = new Handle(timer);
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (!handle.IsDisposed)
            {
                callback();
            }
        };
        timer.Start();
        return handle;
    }

    private sealed class Handle : IDisposable
    {
        private readonly DispatcherTimer _timer;

        public Handle(DispatcherTimer timer)
        {
            _timer = timer;
        }

        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            IsDisposed = true;
            _timer.Stop();
        }
    }
}
