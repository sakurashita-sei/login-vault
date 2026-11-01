using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LoginVault.App.ViewModels;

public enum SplashStepState
{
    Pending,
    Running,
    Done,
}

/// <summary>One startup step shown on the splash screen (label, status text while running, state).</summary>
public sealed partial class SplashStepViewModel : ObservableObject
{
    [ObservableProperty]
    private SplashStepState _state;

    public SplashStepViewModel(string label, string runningText)
    {
        Label = label;
        RunningText = runningText;
    }

    public string Label { get; }

    public string RunningText { get; }

    /// <summary>Short tag rendered in front of the label: OK / RUN / ---.</summary>
    public string Tag => State switch
    {
        SplashStepState.Done => "OK",
        SplashStepState.Running => "RUN",
        _ => "---",
    };

    partial void OnStateChanged(SplashStepState value) => OnPropertyChanged(nameof(Tag));
}

/// <summary>One of the 32 cells of the segmented progress bar.</summary>
public sealed partial class SplashSegmentViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isFilled;
}

/// <summary>
/// Display state of the splash screen: the real startup steps, a 32-segment progress bar and the status
/// line. It performs no I/O; <see cref="App"/> drives it while starting up.
/// </summary>
public sealed partial class SplashViewModel : ObservableObject
{
    public const int SegmentCount = 32;
    public const string ReadyText = "起動の準備ができました";

    [ObservableProperty]
    private string _statusText = "起動しています";

    [ObservableProperty]
    private string _percentText = "000%";

    public SplashViewModel(string version, string runtime)
    {
        // 実際の起動手順と同じ順序（App.StartAsync）
        Steps = new ReadOnlyCollection<SplashStepViewModel>(new[]
        {
            new SplashStepViewModel("起動引数の確認", "起動引数とデータディレクトリを確認しています"),
            new SplashStepViewModel("多重起動の確認", "多重起動を確認しています"),
            new SplashStepViewModel("保管庫の確認", "保管庫ファイルを確認しています"),
            new SplashStepViewModel("画面の準備", "画面を準備しています"),
        });
        Segments = new ReadOnlyCollection<SplashSegmentViewModel>(Enumerable.Range(0, SegmentCount).Select(_ => new SplashSegmentViewModel()).ToArray());
        VersionText = $"v{version} · WPF / {runtime}";
    }

    public IReadOnlyList<SplashStepViewModel> Steps { get; }

    public IReadOnlyList<SplashSegmentViewModel> Segments { get; }

    public string VersionText { get; }

    /// <summary>Marks the steps before <paramref name="index"/> done, that step running and the rest pending.</summary>
    public void StartStep(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Steps.Count);
        for (var i = 0; i < Steps.Count; i++)
        {
            Steps[i].State = i < index ? SplashStepState.Done : i == index ? SplashStepState.Running : SplashStepState.Pending;
        }

        StatusText = Steps[index].RunningText;
        SetProgress(index * 100 / Steps.Count);
    }

    public void FinishAll()
    {
        foreach (var step in Steps)
        {
            step.State = SplashStepState.Done;
        }

        StatusText = ReadyText;
        SetProgress(100);
    }

    private void SetProgress(int percent)
    {
        PercentText = percent.ToString("000", CultureInfo.InvariantCulture) + "%";
        var filled = (int)Math.Round(percent / 100.0 * SegmentCount);
        for (var i = 0; i < Segments.Count; i++)
        {
            Segments[i].IsFilled = i < filled;
        }
    }
}
