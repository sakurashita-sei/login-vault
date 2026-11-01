using LoginVault.App.Locking;
using LoginVault.App.Tests.Support;

namespace LoginVault.App.Tests.Locking;

public class LockControllerTests
{
    private readonly FakeTime _time = new();
    private readonly List<LockReason> _locks = new();
    private int _conceals;

    private LockController CreateRunning(TimeSpan? timeout = null)
    {
        var controller = new LockController(_time, _time) { IdleTimeout = timeout ?? TimeSpan.FromMinutes(5) };
        controller.LockRequested += _locks.Add;
        controller.ConcealRequested += () => _conceals++;
        controller.Start();
        return controller;
    }

    [Fact]
    public void NoActivity_ForIdleTimeout_RequestsLock()
    {
        CreateRunning();

        _time.Advance(TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(1));
        Assert.Empty(_locks);
        _time.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(LockReason.Idle, Assert.Single(_locks));
    }

    [Fact]
    public void Activity_ResetsIdleTimer()
    {
        var controller = CreateRunning();

        _time.Advance(TimeSpan.FromMinutes(4));
        controller.NotifyActivity();
        _time.Advance(TimeSpan.FromMinutes(4));
        Assert.Empty(_locks);
        _time.Advance(TimeSpan.FromMinutes(1));

        Assert.Single(_locks);
    }

    [Fact]
    public void IdleLock_FiresOnlyOnce_UntilRestarted()
    {
        var controller = CreateRunning();

        _time.Advance(TimeSpan.FromMinutes(20));
        Assert.Single(_locks);
        Assert.False(controller.IsRunning);

        controller.Start();
        _time.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(2, _locks.Count);
    }

    [Fact]
    public void SessionLock_RequestsLockImmediately()
    {
        var controller = CreateRunning();

        controller.NotifySessionLocked();

        Assert.Equal(LockReason.SessionLocked, Assert.Single(_locks));
    }

    [Fact]
    public void Suspend_RequestsLockImmediately()
    {
        var controller = CreateRunning();

        controller.NotifySuspend();

        Assert.Equal(LockReason.Suspend, Assert.Single(_locks));
    }

    [Fact]
    public void Deactivation_RequestsConcealButNotLock()
    {
        var controller = CreateRunning();

        controller.NotifyDeactivated();
        _time.Advance(TimeSpan.FromMinutes(1));

        Assert.Equal(1, _conceals);
        Assert.Empty(_locks);
    }

    [Fact]
    public void Stopped_DoesNotRequestLocks()
    {
        var controller = CreateRunning();
        controller.Stop();

        _time.Advance(TimeSpan.FromMinutes(10));
        controller.NotifySessionLocked();

        Assert.Empty(_locks);
    }

    [Fact]
    public void ChangingTimeout_WhileRunning_AppliesToNextCheck()
    {
        var controller = CreateRunning(TimeSpan.FromMinutes(5));

        controller.IdleTimeout = TimeSpan.FromMinutes(1);
        controller.NotifyActivity();
        _time.Advance(TimeSpan.FromMinutes(1));

        Assert.Single(_locks);
    }

    [Fact]
    public void SessionLockedWhileStopped_IsAppliedAsSoonAsTrackingStarts()
    {
        var controller = new LockController(_time, _time);
        controller.LockRequested += _locks.Add;

        controller.NotifySessionLocked();
        Assert.Empty(_locks);
        controller.Start();

        Assert.Equal(LockReason.SessionLocked, Assert.Single(_locks));
        Assert.False(controller.IsRunning);
    }

    [Fact]
    public void SessionUnlockedBeforeStart_DoesNotLock()
    {
        var controller = new LockController(_time, _time);
        controller.LockRequested += _locks.Add;

        controller.NotifySessionLocked();
        controller.NotifySessionUnlocked();
        controller.Start();

        Assert.Empty(_locks);
        Assert.True(controller.IsRunning);
    }

    [Fact]
    public void SuspendWhileStopped_IsAppliedAsSoonAsTrackingStarts()
    {
        var controller = new LockController(_time, _time);
        controller.LockRequested += _locks.Add;

        controller.NotifySuspend();
        Assert.Empty(_locks);
        controller.Start();

        Assert.Equal(LockReason.Suspend, Assert.Single(_locks));
        Assert.False(controller.IsRunning);
    }

    [Fact]
    public void ResumeBeforeTrackingStarts_ClearsThePendingSuspend()
    {
        var controller = new LockController(_time, _time);
        controller.LockRequested += _locks.Add;

        controller.NotifySuspend();
        controller.NotifyResumed();
        controller.Start();

        Assert.Empty(_locks);
        Assert.True(controller.IsRunning);
    }
}
