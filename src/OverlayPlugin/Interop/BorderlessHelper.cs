using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PlayniteOverlay;

/// <summary>
/// Helper class for converting windowed games to borderless fullscreen mode.
/// </summary>
internal static class BorderlessHelper
{
    #region P/Invoke

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SetWindowLong(IntPtr hWnd, int nIndex, uint dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    #endregion

    #region Window Style Constants

    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;

    // Standard Window Styles
    private const uint WS_CAPTION = 0x00C00000;      // Title bar (includes WS_BORDER | WS_DLGFRAME)
    private const uint WS_THICKFRAME = 0x00040000;   // Resizable border
    private const uint WS_SYSMENU = 0x00080000;      // System menu
    private const uint WS_MAXIMIZEBOX = 0x00010000;  // Maximize button
    private const uint WS_MINIMIZEBOX = 0x00020000;  // Minimize button

    // Extended Window Styles
    private const uint WS_EX_DLGMODALFRAME = 0x00000001;
    private const uint WS_EX_WINDOWEDGE = 0x00000100;
    private const uint WS_EX_CLIENTEDGE = 0x00000200;
    private const uint WS_EX_STATICEDGE = 0x00020000;

    // SetWindowPos flags
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_NOOWNERZORDER = 0x0200;
    private const uint SWP_NOSENDCHANGING = 0x0400;
    private const uint SWP_NOZORDER = 0x0004;

    // Styles to remove for borderless
    private const uint STYLES_TO_REMOVE = WS_CAPTION | WS_THICKFRAME | WS_SYSMENU | WS_MAXIMIZEBOX | WS_MINIMIZEBOX;
    private const uint EX_STYLES_TO_REMOVE = WS_EX_DLGMODALFRAME | WS_EX_WINDOWEDGE | WS_EX_CLIENTEDGE | WS_EX_STATICEDGE;

    #endregion

    /// <summary>
    /// Stores the original window state for restoration.
    /// </summary>
    public class WindowState
    {
        public IntPtr Handle { get; set; }
        public uint OriginalStyle { get; set; }
        public uint OriginalExStyle { get; set; }
        public int OriginalX { get; set; }
        public int OriginalY { get; set; }
        public int OriginalWidth { get; set; }
        public int OriginalHeight { get; set; }
    }

    /// <summary>
    /// Checks if a window has visible borders (title bar, thick frame).
    /// </summary>
    public static bool HasWindowBorders(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !IsWindowVisible(hwnd))
            return false;

        var style = GetWindowLong(hwnd, GWL_STYLE);
        return (style & WS_CAPTION) != 0 || (style & WS_THICKFRAME) != 0;
    }

    /// <summary>
    /// Checks if a window appears to be in exclusive fullscreen mode.
    /// This is a heuristic - it checks if the window covers the entire screen without borders.
    /// </summary>
    public static bool IsLikelyExclusiveFullscreen(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !IsWindowVisible(hwnd))
            return false;

        var style = GetWindowLong(hwnd, GWL_STYLE);
        bool hasBorders = (style & WS_CAPTION) != 0 || (style & WS_THICKFRAME) != 0;

        if (hasBorders)
            return false; // Has borders, so it's windowed

        // Check if window covers the entire screen
        if (!GetWindowRect(hwnd, out var rect))
            return false;

        var screen = Screen.FromHandle(hwnd);
        return rect.Left == screen.Bounds.Left &&
               rect.Top == screen.Bounds.Top &&
               rect.Right - rect.Left == screen.Bounds.Width &&
               rect.Bottom - rect.Top == screen.Bounds.Height;
    }

    /// <summary>
    /// Converts a windowed game to borderless fullscreen mode.
    /// Returns the original window state for later restoration, or null if conversion failed.
    /// </summary>
    public static WindowState? MakeBorderless(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !IsWindowVisible(hwnd))
            return null;

        // Check if window has borders to remove
        if (!HasWindowBorders(hwnd))
            return null; // Already borderless or invisible

        // Save original state
        var state = new WindowState
        {
            Handle = hwnd,
            OriginalStyle = GetWindowLong(hwnd, GWL_STYLE),
            OriginalExStyle = GetWindowLong(hwnd, GWL_EXSTYLE)
        };

        if (GetWindowRect(hwnd, out var rect))
        {
            state.OriginalX = rect.Left;
            state.OriginalY = rect.Top;
            state.OriginalWidth = rect.Right - rect.Left;
            state.OriginalHeight = rect.Bottom - rect.Top;
        }

        // Calculate new styles (remove borders)
        uint newStyle = state.OriginalStyle & ~STYLES_TO_REMOVE;
        uint newExStyle = state.OriginalExStyle & ~EX_STYLES_TO_REMOVE;

        // Apply new styles
        SetWindowLong(hwnd, GWL_STYLE, newStyle);
        SetWindowLong(hwnd, GWL_EXSTYLE, newExStyle);

        // Get the screen the window is on and resize to cover it
        var screen = Screen.FromHandle(hwnd);

        SetWindowPos(
            hwnd,
            IntPtr.Zero,
            screen.Bounds.X,
            screen.Bounds.Y,
            screen.Bounds.Width,
            screen.Bounds.Height,
            SWP_FRAMECHANGED | SWP_SHOWWINDOW | SWP_NOOWNERZORDER | SWP_NOSENDCHANGING | SWP_NOZORDER
        );

        return state;
    }

    /// <summary>
    /// Restores a window to its original state before borderless conversion.
    /// </summary>
    public static bool RestoreWindow(WindowState? state)
    {
        if (state == null || state.Handle == IntPtr.Zero)
            return false;

        try
        {
            // Check if window still exists
            if (!IsWindowVisible(state.Handle))
                return false;

            // Restore original styles
            SetWindowLong(state.Handle, GWL_STYLE, state.OriginalStyle);
            SetWindowLong(state.Handle, GWL_EXSTYLE, state.OriginalExStyle);

            // Restore original position and size
            SetWindowPos(
                state.Handle,
                IntPtr.Zero,
                state.OriginalX,
                state.OriginalY,
                state.OriginalWidth,
                state.OriginalHeight,
                SWP_FRAMECHANGED | SWP_SHOWWINDOW | SWP_NOOWNERZORDER | SWP_NOZORDER
            );

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets the main window handle for a process.
    /// </summary>
    public static IntPtr GetMainWindowHandle(int processId)
    {
        try
        {
            var process = Process.GetProcessById(processId);
            return process.MainWindowHandle;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }
}
