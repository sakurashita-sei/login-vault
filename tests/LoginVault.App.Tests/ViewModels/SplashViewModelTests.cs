using LoginVault.App.ViewModels;

namespace LoginVault.App.Tests.ViewModels;

public class SplashViewModelTests
{
    [Fact]
    public void Initially_AllStepsPending_AndZeroPercent()
    {
        var vm = new SplashViewModel("0.1.0", ".NET 8");

        Assert.Equal(4, vm.Steps.Count);
        Assert.All(vm.Steps, s => Assert.Equal(SplashStepState.Pending, s.State));
        Assert.All(vm.Steps, s => Assert.Equal("---", s.Tag));
        Assert.Equal("000%", vm.PercentText);
        Assert.Equal(32, vm.Segments.Count);
        Assert.All(vm.Segments, s => Assert.False(s.IsFilled));
        Assert.Equal("v0.1.0 · WPF / .NET 8", vm.VersionText);
    }

    [Fact]
    public void StartStep_MarksEarlierStepsDone_CurrentRunning_RestPending()
    {
        var vm = new SplashViewModel("0.1.0", ".NET 8");

        vm.StartStep(2);

        Assert.Equal(new[] { "OK", "OK", "RUN", "---" }, vm.Steps.Select(s => s.Tag));
        Assert.Equal(SplashStepState.Running, vm.Steps[2].State);
        Assert.Equal(vm.Steps[2].RunningText, vm.StatusText);
        Assert.Equal("050%", vm.PercentText);
        Assert.Equal(16, vm.Segments.Count(s => s.IsFilled));
        // 先頭から順に埋まる
        Assert.True(vm.Segments.Take(16).All(s => s.IsFilled));
        Assert.True(vm.Segments.Skip(16).All(s => !s.IsFilled));
    }

    [Fact]
    public void FinishAll_MarksEverythingDone_AndReportsReady()
    {
        var vm = new SplashViewModel("0.1.0", ".NET 8");
        vm.StartStep(3);

        vm.FinishAll();

        Assert.All(vm.Steps, s => Assert.Equal(SplashStepState.Done, s.State));
        Assert.All(vm.Steps, s => Assert.Equal("OK", s.Tag));
        Assert.Equal("100%", vm.PercentText);
        Assert.All(vm.Segments, s => Assert.True(s.IsFilled));
        Assert.Equal("起動の準備ができました", vm.StatusText);
    }

    [Fact]
    public void StartStep_OutOfRange_Throws()
    {
        var vm = new SplashViewModel("0.1.0", ".NET 8");

        Assert.Throws<ArgumentOutOfRangeException>(() => vm.StartStep(4));
        Assert.Throws<ArgumentOutOfRangeException>(() => vm.StartStep(-1));
    }

    [Fact]
    public void Steps_DescribeTheRealStartupSequence()
    {
        var vm = new SplashViewModel("0.1.0", ".NET 8");

        Assert.Equal(new[] { "起動引数の確認", "多重起動の確認", "保管庫の確認", "画面の準備" }, vm.Steps.Select(s => s.Label));
    }
}
