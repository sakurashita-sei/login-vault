using System.Windows;
using System.Windows.Input;
using LoginVault.App.Locking;

namespace LoginVault.App.Tests.Locking;

/// <summary>
/// The idle timer must only be reset by real user input. WPF re-raises MouseMove with an unchanged
/// position after layout updates while the pointer rests on the window, which must not count.
/// </summary>
public class InputActivityMonitorTests
{
    [Fact]
    public void MouseMove_AtSamePosition_IsNotActivity_ButMovingIs()
    {
        RunOnSta(() =>
        {
            Point? last = null;
            var move = new MouseEventArgs(Mouse.PrimaryDevice, 0);

            Assert.True(InputActivityMonitor.IsUserActivity(move, new Point(10, 10), ref last));
            Assert.False(InputActivityMonitor.IsUserActivity(move, new Point(10, 10), ref last));
            Assert.False(InputActivityMonitor.IsUserActivity(move, new Point(10, 10), ref last));
            Assert.True(InputActivityMonitor.IsUserActivity(move, new Point(11, 10), ref last));
        });
    }

    [Fact]
    public void ButtonsAndWheel_AlwaysCount_EvenWithoutMovement()
    {
        RunOnSta(() =>
        {
            Point? last = new Point(10, 10);

            Assert.True(InputActivityMonitor.IsUserActivity(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left), new Point(10, 10), ref last));
            Assert.True(InputActivityMonitor.IsUserActivity(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, 120), new Point(10, 10), ref last));
        });
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
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
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw new Xunit.Sdk.XunitException(failure.ToString());
        }
    }

    [Fact]
    public void KeyboardFocusChange_IsNotActivity_ButTextInputIs()
    {
        RunOnSta(() =>
        {
            Point? last = null;
            var focusChange = new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, null, null);
            var text = new TextCompositionEventArgs(Keyboard.PrimaryDevice, new TextComposition(InputManager.Current, null!, "a"));

            Assert.False(InputActivityMonitor.IsUserActivity(focusChange, default, ref last));
            Assert.True(InputActivityMonitor.IsUserActivity(text, default, ref last));
        });
    }
}
