using System.Windows;

namespace LoginVault.App.Views;

/// <summary>Keeps a window's initial size and position inside the primary work area (e.g. 150% scaling on a laptop).</summary>
public static class WindowSizing
{
    public static void FitToWorkArea(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var area = SystemParameters.WorkArea;
        if (window.Height > area.Height)
        {
            window.Height = area.Height;
        }

        if (window.Width > area.Width)
        {
            window.Width = area.Width;
        }

        if (window.Top + window.Height > area.Bottom)
        {
            window.Top = Math.Max(area.Top, area.Bottom - window.Height);
        }

        if (window.Left + window.Width > area.Right)
        {
            window.Left = Math.Max(area.Left, area.Right - window.Width);
        }
    }

    /// <summary>Moves a window that sizes to its content back inside the work area after it grew (uses the actual size).</summary>
    public static void KeepInWorkArea(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var area = SystemParameters.WorkArea;
        if (window.Top + window.ActualHeight > area.Bottom)
        {
            window.Top = Math.Max(area.Top, area.Bottom - window.ActualHeight);
        }

        if (window.Left + window.ActualWidth > area.Right)
        {
            window.Left = Math.Max(area.Left, area.Right - window.ActualWidth);
        }
    }
}
