using LoginVault.App.Clipboard;
using LoginVault.App.Tests.Support;

namespace LoginVault.App.Tests.Clipboard;

public class ClipboardGuardTests
{
    private static readonly TimeSpan ClearAfter = TimeSpan.FromSeconds(30);
    private readonly FakeTime _time = new();
    private readonly FakeClipboardDevice _device = new();
    private readonly List<TimeSpan> _delays = new();

    private ClipboardGuard CreateGuard() => new(_device, _time, delay =>
    {
        _delays.Add(delay);
        return Task.CompletedTask;
    });

    [Fact]
    public async Task Copy_PlacesTextWithExclusionFormatsAndToken()
    {
        var guard = CreateGuard();

        var outcome = await guard.CopyAsync("secret-架空", ClearAfter);

        Assert.Equal(CopyOutcome.Copied, outcome);
        var payload = Assert.Single(_device.SetHistory);
        Assert.Equal("secret-架空", payload.Text);
        Assert.Contains(payload.RegisteredFormats, f => f.FormatName == "ExcludeClipboardContentFromMonitorProcessing");
        Assert.Contains(payload.RegisteredFormats, f => f.FormatName == ClipboardPayload.TokenFormat);
        Assert.Equal(payload.Token, guard.CurrentToken);
    }

    [Fact]
    public async Task Copy_WhenClipboardBusy_RetriesThenSucceeds()
    {
        _device.BusySetCount = 2;
        var guard = CreateGuard();

        var outcome = await guard.CopyAsync("x", ClearAfter);

        Assert.Equal(CopyOutcome.Copied, outcome);
        Assert.Equal(3, _device.SetAttempts);
        Assert.Equal(2, _delays.Count);
    }

    [Fact]
    public async Task Copy_WhenClipboardStaysBusy_GivesUpAfterLimitedAttempts()
    {
        _device.BusySetCount = 100;
        var guard = CreateGuard();

        var outcome = await guard.CopyAsync("x", ClearAfter);

        Assert.Equal(CopyOutcome.Busy, outcome);
        Assert.Equal(ClipboardGuard.MaxSetAttempts, _device.SetAttempts);
        Assert.Null(_device.Current);
        Assert.Null(guard.CurrentToken);
        Assert.Equal(0, _time.PendingTimers);
    }

    [Fact]
    public async Task Copy_WhenSetFails_ReportsFailure()
    {
        _device.FailSet = true;
        var guard = CreateGuard();

        var outcome = await guard.CopyAsync("x", ClearAfter);

        Assert.Equal(CopyOutcome.Failed, outcome);
        Assert.Null(guard.CurrentToken);
    }

    [Fact]
    public async Task AfterClearDelay_OwnCopyIsCleared()
    {
        var guard = CreateGuard();
        await guard.CopyAsync("x", ClearAfter);

        _time.Advance(ClearAfter - TimeSpan.FromSeconds(1));
        Assert.Equal("x", _device.CurrentText);
        _time.Advance(TimeSpan.FromSeconds(1));

        Assert.Null(_device.Current);
        Assert.Null(guard.CurrentToken);
    }

    [Fact]
    public async Task AfterClearDelay_ForeignContentIsLeftAlone()
    {
        var guard = CreateGuard();
        await guard.CopyAsync("x", ClearAfter);
        _device.SimulateForeignCopy();

        _time.Advance(ClearAfter);

        Assert.True(_device.HoldsForeignContent);
    }

    [Fact]
    public async Task SecondCopy_IsNotClearedByFirstCopysTimer()
    {
        var guard = CreateGuard();
        await guard.CopyAsync("first", ClearAfter);
        _time.Advance(TimeSpan.FromSeconds(20));
        await guard.CopyAsync("second", ClearAfter);

        _time.Advance(TimeSpan.FromSeconds(15));
        Assert.Equal("second", _device.CurrentText);

        _time.Advance(TimeSpan.FromSeconds(15));
        Assert.Null(_device.Current);
    }

    [Fact]
    public async Task ClearNowIfOwn_ClearsOwnCopy_AndCancelsTimer()
    {
        var guard = CreateGuard();
        await guard.CopyAsync("x", ClearAfter);

        var result = await guard.ClearNowIfOwnAsync();

        Assert.Equal(ClipboardClearResult.Cleared, result);
        Assert.Null(_device.Current);
        Assert.Equal(0, _time.PendingTimers);
    }

    [Fact]
    public async Task ClearNowIfOwn_LeavesForeignContent()
    {
        var guard = CreateGuard();
        await guard.CopyAsync("x", ClearAfter);
        _device.SimulateForeignCopy();

        var result = await guard.ClearNowIfOwnAsync();

        Assert.Equal(ClipboardClearResult.NotOurs, result);
        Assert.True(_device.HoldsForeignContent);
    }

    [Fact]
    public async Task ClearNowIfOwn_WithoutPriorCopy_DoesNothing()
    {
        var guard = CreateGuard();

        var result = await guard.ClearNowIfOwnAsync();

        Assert.Equal(ClipboardClearResult.NotOurs, result);
        Assert.Equal(0, _device.ClearAttempts);
    }

    [Fact]
    public async Task TimerClear_WhenBusy_RetriesLimitedTimes()
    {
        _device.BusyClearCount = 100;
        var guard = CreateGuard();
        var results = new List<ClipboardClearResult>();
        guard.ClearAttempted += results.Add;
        await guard.CopyAsync("x", ClearAfter);

        _time.Advance(ClearAfter);

        Assert.Equal(ClipboardGuard.MaxClearAttempts, _device.ClearAttempts);
        Assert.Equal(ClipboardClearResult.Busy, Assert.Single(results));
    }

    [Fact]
    public async Task TimerClear_WhenBusyThenFree_Clears()
    {
        _device.BusyClearCount = 1;
        var guard = CreateGuard();
        await guard.CopyAsync("x", ClearAfter);

        _time.Advance(ClearAfter);

        Assert.Null(_device.Current);
        Assert.Equal(2, _device.ClearAttempts);
    }

    [Fact]
    public async Task ClearNowIfOwn_MakesFirstAttemptOnCallerThread_EvenWithRealDelays()
    {
        _device.BusyClearCount = 1;
        var guard = new ClipboardGuard(_device, _time, async _ => await Task.Yield());
        await guard.CopyAsync("x", ClearAfter);
        var callerThread = Environment.CurrentManagedThreadId;

        var result = await guard.ClearNowIfOwnAsync();

        Assert.Equal(ClipboardClearResult.Cleared, result);
        Assert.Equal(callerThread, _device.FirstClearThreadId);
    }

    [Fact]
    public async Task TimerClear_WhenDeviceThrows_ReportsFailureInsteadOfCrashing()
    {
        var guard = CreateGuard();
        var results = new List<ClipboardClearResult>();
        guard.ClearAttempted += results.Add;
        await guard.CopyAsync("x", ClearAfter);
        _device.ThrowOnClear = true;

        _time.Advance(ClearAfter);

        Assert.Equal(ClipboardClearResult.Failed, Assert.Single(results));
    }

    [Fact]
    public async Task ClearNowIfOwn_WhenBusy_KeepsTheToken_SoTheNextAttemptCanStillClear()
    {
        var guard = CreateGuard();
        await guard.CopyAsync("x", ClearAfter);
        _device.BusyClearCount = 100;

        var first = await guard.ClearNowIfOwnAsync();

        Assert.Equal(ClipboardClearResult.Busy, first);
        Assert.NotNull(guard.CurrentToken);
        _device.BusyClearCount = 0;
        Assert.Equal(ClipboardClearResult.Cleared, await guard.ClearNowIfOwnAsync());
        Assert.Null(_device.Current);
        Assert.Null(guard.CurrentToken);
    }

    [Fact]
    public async Task TimerClear_WhenBusy_KeepsTheToken_SoLockOrExitCanClearLater()
    {
        var guard = CreateGuard();
        await guard.CopyAsync("x", ClearAfter);
        _device.BusyClearCount = 100;

        _time.Advance(ClearAfter);

        Assert.NotNull(guard.CurrentToken);
        _device.BusyClearCount = 0;
        Assert.Equal(ClipboardClearResult.Cleared, await guard.ClearNowIfOwnAsync());
        Assert.Null(_device.Current);
    }

    [Fact]
    public async Task Copy_RetryingWhileBusy_IsAbandoned_WhenALockClearsTheClipboardInBetween()
    {
        // Busy の再試行待ちの間にロック（ClearNowIfOwn）が走ったら、その後に書き込めても秘密を残さない
        _device.BusySetCount = 1;
        ClipboardGuard? guard = null;
        guard = new ClipboardGuard(_device, _time, async _ => await guard!.ClearNowIfOwnAsync());

        var outcome = await guard.CopyAsync("secret-架空", ClearAfter);

        Assert.NotEqual(CopyOutcome.Copied, outcome);
        Assert.Null(_device.Current);
        Assert.Null(guard.CurrentToken);
        Assert.Equal(0, _time.PendingTimers);
    }
}
