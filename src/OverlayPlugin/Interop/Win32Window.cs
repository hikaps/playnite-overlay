using System;
using System.Runtime.InteropServices;

namespace PlayniteOverlay;

internal static class Win32Window
{
    private const int SW_RESTORE = 9;
    private const int SW_MINIMIZE = 6;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    public static IntPtr GetForegroundWindowHandle()
    {
        return GetForegroundWindow();
    }

    public static void Minimize(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            return;
        }

        try
        {
            ShowWindow(hWnd, SW_MINIMIZE);
        }
        catch
        {
            // ignore
        }
    }

    public static void RestoreAndActivate(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            return;
        }

        try
        {
            // Only restore if minimized - don't touch maximized windows
            if (IsIconic(hWnd))
            {
                ShowWindow(hWnd, SW_RESTORE);
            }

            // Bring to foreground
            SetForegroundWindow(hWnd);
        }
        catch
        {
            // ignore
        }
    }
}
