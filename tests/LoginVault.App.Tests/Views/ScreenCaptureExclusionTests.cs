using System.Windows;
using System.Windows.Interop;
using LoginVault.App.Tests.Support;
using LoginVault.App.Views;

namespace LoginVault.App.Tests.Views;

/// <summary>The exclusion must really reach the window handles (SetWindowDisplayAffinity), not just be remembered.</summary>
public class ScreenCaptureExclusionTests
{
    [Fact]
    public void Apply_SetsAndClearsTheDisplayAffinity_OfExistingWindows()
    {
        UiThread.Invoke(() =>
        {
            UiThread.EnsureApplication();
            var application = Application.Current!;
            // 画面外・非アクティブ・タスクバー非表示の 1px ウィンドウ。ハンドルだけ作り、表示はしない
            var window = new Window
            {
                ShowInTaskbar = false,
                ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -32000,
                Top = -32000,
                Width = 1,
                Height = 1,
            };
            try
            {
                new WindowInteropHelper(window).EnsureHandle();

                ScreenCaptureExclusion.Apply(application, true);
                Assert.True(ScreenCaptureExclusion.IsEnabled);
                Assert.Equal(true, ScreenCaptureExclusion.ReadWindowState(window));

                ScreenCaptureExclusion.Apply(application, false);
                Assert.False(ScreenCaptureExclusion.IsEnabled);
                Assert.Equal(false, ScreenCaptureExclusion.ReadWindowState(window));
            }
            finally
            {
                ScreenCaptureExclusion.Apply(application, false);
                window.Close();
            }
        });
    }

    /// <summary>A 1px, borderless, non-activated window far off-screen: showing it touches nothing the user can see.</summary>
    private static Window OffScreenWindow() => new()
    {
        ShowInTaskbar = false,
        ShowActivated = false,
        WindowStyle = WindowStyle.None,
        ResizeMode = ResizeMode.NoResize,
        WindowStartupLocation = WindowStartupLocation.Manual,
        Left = -32000,
        Top = -32000,
        Width = 1,
        Height = 1,
    };

    [Fact]
    public void WindowsShownWhileEnabled_AreExcludedBeforeTheyBecomeVisible_AndNotAfterDisabling()
    {
        UiThread.Invoke(() =>
        {
            UiThread.EnsureApplication();
            var application = Application.Current!;
            var shownWhileOn = OffScreenWindow();
            var shownWhileOff = OffScreenWindow();
            try
            {
                ScreenCaptureExclusion.Apply(application, true);
                shownWhileOn.Show();
                // Show は同期で戻り、Loaded はまだ来ていない（Loaded 優先度で後から処理される）。表示の時点で除外済みであること。
                // メニュー・ドロップダウン・ツールヒントのポップアップや共通ダイアログも同じく「表示の直前」に除外される
                Assert.Equal(true, ScreenCaptureExclusion.ReadWindowState(shownWhileOn));

                ScreenCaptureExclusion.Apply(application, false);
                Assert.Equal(false, ScreenCaptureExclusion.ReadWindowState(shownWhileOn));
                shownWhileOff.Show();
                Assert.Equal(false, ScreenCaptureExclusion.ReadWindowState(shownWhileOff));
            }
            finally
            {
                ScreenCaptureExclusion.Apply(application, false);
                shownWhileOn.Close();
                shownWhileOff.Close();
            }
        });
    }
}
