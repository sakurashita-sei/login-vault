using System.Windows;
using System.Windows.Media.Animation;
using LoginVault.App.ViewModels;

namespace LoginVault.App.Views;

/// <summary>Borderless splash shown while the app starts. Code-behind only starts the decorative animations.</summary>
public partial class SplashWindow : Window
{
    public SplashWindow(SplashViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Windows の「アニメーション効果」が無効なら動かさない
        if (!SystemParameters.ClientAreaAnimation)
        {
            return;
        }

        RingOuterRotate.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(18)) { RepeatBehavior = RepeatBehavior.Forever });
        RingMidRotate.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, new DoubleAnimation(0, -360, TimeSpan.FromSeconds(9)) { RepeatBehavior = RepeatBehavior.Forever });
        SweepTransform.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, new DoubleAnimation(-40, ActualHeight + 40, TimeSpan.FromSeconds(3.2)) { RepeatBehavior = RepeatBehavior.Forever });
        var blink = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, Duration = TimeSpan.FromSeconds(1.2) };
        blink.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        blink.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.25, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.6))));
        StatusCursor.BeginAnimation(OpacityProperty, blink);
    }
}
