using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;

namespace LoginVault.App.Locking;

/// <summary>Forwards Windows session lock and sleep notifications to the <see cref="LockController"/> on the UI thread.</summary>
public sealed class SystemEventsLockSignals : IDisposable
{
    private readonly LockController _controller;
    private readonly Dispatcher _dispatcher;

    public SystemEventsLockSignals(LockController controller, Dispatcher dispatcher)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    public void Dispose()
    {
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect or SessionSwitchReason.SessionLogoff)
        {
            _dispatcher.BeginInvoke(_controller.NotifySessionLocked);
        }
        else if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect)
        {
            _dispatcher.BeginInvoke(_controller.NotifySessionUnlocked);
        }
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend)
        {
            // スリープ時はディスパッチャーが止まる前に同期的にロック要求を出す
            _dispatcher.Invoke(_controller.NotifySuspend);
        }
        else if (e.Mode == PowerModes.Resume)
        {
            _dispatcher.BeginInvoke(_controller.NotifyResumed);
        }
    }
}

/// <summary>
/// Counts key presses, text input, mouse buttons, wheel and real pointer movement anywhere in the app
/// (dialogs included) as activity. Ignored: a MouseMove whose position has not changed (WPF re-raises it
/// after layout updates while the pointer merely rests on the window) and keyboard focus changes (other
/// applications can cause them). Neither must keep the vault unlocked.
/// </summary>
public sealed class InputActivityMonitor : IDisposable
{
    private readonly LockController _controller;
    private readonly InputManager _inputManager;
    private System.Windows.Point? _lastMousePosition;

    public InputActivityMonitor(LockController controller, InputManager inputManager)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _inputManager = inputManager ?? throw new ArgumentNullException(nameof(inputManager));
        _inputManager.PreProcessInput += OnPreProcessInput;
    }

    public void Dispose() => _inputManager.PreProcessInput -= OnPreProcessInput;

    /// <summary>Decides whether an input event is user activity; <paramref name="lastMousePosition"/> is updated on a real move.</summary>
    public static bool IsUserActivity(InputEventArgs input, System.Windows.Point mousePosition, ref System.Windows.Point? lastMousePosition)
    {
        switch (input)
        {
            case KeyEventArgs or TextCompositionEventArgs or MouseButtonEventArgs or MouseWheelEventArgs:
                return true;
            case KeyboardEventArgs:
                // フォーカスの移動（KeyboardFocusChangedEventArgs）は他アプリの動作でも起きるため操作と見なさない
                return false;
            case MouseEventArgs:
                // 位置が変わらない MouseMove（レイアウト更新後に WPF が合成するもの）は操作と見なさない
                if (lastMousePosition == mousePosition)
                {
                    return false;
                }

                lastMousePosition = mousePosition;
                return true;
            default:
                return false;
        }
    }

    private void OnPreProcessInput(object sender, PreProcessInputEventArgs e)
    {
        if (e.StagingItem.Input is InputEventArgs input && IsUserActivity(input, input is MouseEventArgs mouse ? mouse.GetPosition(null) : default, ref _lastMousePosition))
        {
            _controller.NotifyActivity();
        }
    }
}
