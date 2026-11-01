using LoginVault.Core.Time;

namespace LoginVault.App.Locking;

public enum LockReason
{
    Manual,
    Idle,
    SessionLocked,
    Suspend,
    Exit,
}

/// <summary>
/// Decides when the vault must lock (idle timeout measured by in-app activity, Windows session lock,
/// sleep) or only conceal revealed secrets (window deactivation). It raises events; the shell acts.
/// </summary>
public sealed class LockController
{
    private readonly IClock _clock;
    private readonly ITimerScheduler _scheduler;
    private TimeSpan _idleTimeout = TimeSpan.FromMinutes(5);
    private DateTimeOffset _lastActivity;
    private IDisposable? _check;
    private bool _sessionLocked;
    private bool _suspendPending;

    public LockController(IClock clock, ITimerScheduler scheduler)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
    }

    public event Action<LockReason>? LockRequested;

    public event Action? ConcealRequested;

    public bool IsRunning { get; private set; }

    public TimeSpan IdleTimeout
    {
        get => _idleTimeout;
        set
        {
            if (value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            _idleTimeout = value;
            if (IsRunning)
            {
                ScheduleCheck();
            }
        }
    }

    /// <summary>Starts idle tracking (call after unlock). If Windows locked the session meanwhile, the lock is requested right away.</summary>
    public void Start()
    {
        _lastActivity = _clock.UtcNow;
        IsRunning = true;
        if (_sessionLocked)
        {
            // 解錠処理中に Windows がロックされた場合は、開始と同時にロックへ戻す
            RequestLock(LockReason.SessionLocked);
            return;
        }

        if (_suspendPending)
        {
            // 解錠処理中にスリープした場合も同じ
            _suspendPending = false;
            RequestLock(LockReason.Suspend);
            return;
        }

        ScheduleCheck();
    }

    /// <summary>Stops idle tracking (call on lock). Signals while stopped are ignored.</summary>
    public void Stop()
    {
        IsRunning = false;
        _check?.Dispose();
        _check = null;
    }

    /// <summary>Any in-app input, including dialogs, counts as activity.</summary>
    public void NotifyActivity()
    {
        if (IsRunning)
        {
            _lastActivity = _clock.UtcNow;
        }
    }

    /// <summary>Windows session locked. Remembered even while stopped so an unlock finishing afterwards locks again.</summary>
    public void NotifySessionLocked()
    {
        _sessionLocked = true;
        RequestLock(LockReason.SessionLocked);
    }

    /// <summary>Windows session unlocked (or reconnected).</summary>
    public void NotifySessionUnlocked() => _sessionLocked = false;

    /// <summary>Entering sleep. Remembered while stopped (an unlock in progress) and applied when tracking starts, unless the system resumes first.</summary>
    public void NotifySuspend()
    {
        if (!IsRunning)
        {
            _suspendPending = true;
            return;
        }

        RequestLock(LockReason.Suspend);
    }

    /// <summary>Resumed from sleep: a suspend that arrived while stopped no longer forces a lock.</summary>
    public void NotifyResumed() => _suspendPending = false;

    /// <summary>The window lost focus: hide revealed secrets but keep the vault open.</summary>
    public void NotifyDeactivated()
    {
        if (IsRunning)
        {
            ConcealRequested?.Invoke();
        }
    }

    private void RequestLock(LockReason reason)
    {
        if (!IsRunning)
        {
            return;
        }

        Stop();
        LockRequested?.Invoke(reason);
    }

    private void ScheduleCheck()
    {
        _check?.Dispose();
        var remaining = _lastActivity + _idleTimeout - _clock.UtcNow;
        _check = _scheduler.Schedule(remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining, OnCheck);
    }

    private void OnCheck()
    {
        if (!IsRunning)
        {
            return;
        }

        var idle = _clock.UtcNow - _lastActivity;
        if (idle >= _idleTimeout)
        {
            RequestLock(LockReason.Idle);
        }
        else
        {
            ScheduleCheck();
        }
    }
}
