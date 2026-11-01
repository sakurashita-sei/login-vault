using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LoginVault.App.Views;

/// <summary>
/// Hides the app from screen capture (screen sharing, recording, screenshots) with SetWindowDisplayAffinity while
/// the vault preference is on. It covers every top-level window of the UI thread: the main window, dialogs, the
/// pop-ups of menus, drop-downs and tool tips, and the Win32 file dialogs. Existing windows are switched at once;
/// windows shown later are excluded just before they become visible, through a thread-local WH_CALLWNDPROC hook
/// that watches WM_WINDOWPOSCHANGING with SWP_SHOWWINDOW (a Loaded handler runs only after the first frame is on
/// screen, so it stays only as a fallback). Off by default; the choice lives inside the encrypted vault, so it takes
/// effect after the first unlock of a session and stays on for that session's lock screen (D-038, D-044).
/// </summary>
public static class ScreenCaptureExclusion
{
    private const uint WdaNone = 0x0;

    /// <summary>Windows 7 and later: the window is black in captures.</summary>
    private const uint WdaMonitor = 0x1;

    /// <summary>Windows 10 version 2004 and later: the window does not appear in captures at all.</summary>
    private const uint WdaExcludeFromCapture = 0x11;

    private const int WhCallWndProc = 4;
    private const uint WmWindowPosChanging = 0x0046;
    private const uint SwpShowWindow = 0x0040;
    private const uint GaRoot = 2;

    // ネイティブ側が呼び続けるため、フックの委譲は静的に保持して GC に回収させない
    private static readonly HookProc CallWndProcHook = OnCallWndProc;
    private static IntPtr _hook;
    private static bool _loadedFallbackRegistered;

    /// <summary>The value most recently passed to <see cref="Apply"/>.</summary>
    public static bool IsEnabled { get; private set; }

    /// <summary>
    /// Remembers the choice and applies it to every top-level window of the calling thread (call on the UI thread of
    /// <paramref name="app"/>); while on, windows shown later on that thread are excluded before they appear.
    /// </summary>
    public static void Apply(Application app, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(app);
        IsEnabled = enabled;
        if (enabled)
        {
            InstallHook();
        }
        else
        {
            RemoveHook();
        }

        RegisterLoadedFallback();
        // 既にあるウィンドウ（開いているメニューやドロップダウンのポップアップも含む）をまとめて切り替える
        _ = EnumThreadWindows(GetCurrentThreadId(), (hwnd, _) =>
        {
            SetAffinity(hwnd, enabled);
            return true;
        }, IntPtr.Zero);
    }

    /// <summary>Applies the current choice to one window (used by the Loaded fallback).</summary>
    public static void ApplyToWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var handle = new WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero)
        {
            SetAffinity(handle, IsEnabled);
        }
    }

    /// <summary>Reads the affinity back from the window: true when excluded, false when not, null when the window has no handle or the call fails.</summary>
    public static bool? ReadWindowState(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        return GetWindowDisplayAffinity(handle, out var affinity) ? affinity != WdaNone : null;
    }

    private static void SetAffinity(IntPtr hwnd, bool exclude)
    {
        if (!exclude)
        {
            _ = SetWindowDisplayAffinity(hwnd, WdaNone);
            return;
        }

        // 新しい方式が使えない古い Windows では黒塗り方式に落とす。どちらも失敗する環境（対応外）では何もしない
        if (!SetWindowDisplayAffinity(hwnd, WdaExcludeFromCapture))
        {
            _ = SetWindowDisplayAffinity(hwnd, WdaMonitor);
        }
    }

    private static void InstallHook()
    {
        if (_hook != IntPtr.Zero)
        {
            return;
        }

        // 呼び出したスレッドだけを対象にするフック（モジュールハンドル不要）。失敗しても Loaded の後追い適用は残る
        _hook = SetWindowsHookEx(WhCallWndProc, CallWndProcHook, IntPtr.Zero, GetCurrentThreadId());
    }

    private static void RemoveHook()
    {
        if (_hook == IntPtr.Zero)
        {
            return;
        }

        _ = UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private static void RegisterLoadedFallback()
    {
        if (_loadedFallbackRegistered)
        {
            return;
        }

        _loadedFallbackRegistered = true;
        // フックを入れられなかった場合の保険: ハンドル生成後（表示後）に現在の選択を適用する
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, _) =>
        {
            if (sender is Window window)
            {
                ApplyToWindow(window);
            }
        }));
    }

    /// <summary>Excludes a top-level window of this thread at the moment it is about to be shown (before anything is drawn).</summary>
    private static IntPtr OnCallWndProc(int code, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (code >= 0 && IsEnabled && lParam != IntPtr.Zero)
            {
                var message = Marshal.PtrToStructure<CallWndProcMessage>(lParam);
                if (message.Message == WmWindowPosChanging && message.LParam != IntPtr.Zero)
                {
                    var position = Marshal.PtrToStructure<WindowPosition>(message.LParam);
                    if ((position.Flags & SwpShowWindow) != 0 && GetAncestor(message.Hwnd, GaRoot) == message.Hwnd)
                    {
                        SetAffinity(message.Hwnd, exclude: true);
                    }
                }
            }
        }
        catch (Exception)
        {
            // フック内の例外はネイティブ側でプロセスを落とすため、ここで止める（Loaded の後追い適用は残る）
        }

        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    private delegate bool EnumThreadWindowsProc(IntPtr hwnd, IntPtr lParam);

    /// <summary>CWPSTRUCT.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct CallWndProcMessage
    {
        public IntPtr LParam;
        public IntPtr WParam;
        public uint Message;
        public IntPtr Hwnd;
    }

    /// <summary>WINDOWPOS.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPosition
    {
        public IntPtr Hwnd;
        public IntPtr HwndInsertAfter;
        public int X;
        public int Y;
        public int Cx;
        public int Cy;
        public uint Flags;
    }

    // DLL の検索パスはアセンブリ属性（AssemblyInfo.cs）でシステムディレクトリに限定している
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowDisplayAffinity(IntPtr hwnd, out uint affinity);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc callback, IntPtr module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumThreadWindows(uint threadId, EnumThreadWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
