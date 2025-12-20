using System;
using System.Runtime.InteropServices;

namespace PlayniteOverlay;

internal static class Win32Window
{
    private const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    public static void RestoreAndActivate(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            return;
        }

        try
        {
            // Always restore - handles both minimized and hidden (tray) windows
            ShowWindow(hWnd, SW_RESTORE);

            // Bring to foreground
            SetForegroundWindow(hWnd);
        }
        catch
        {
            // ignore
        }
    }
}
