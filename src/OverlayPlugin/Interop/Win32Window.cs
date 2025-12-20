using System;
using System.Runtime.InteropServices;

namespace PlayniteOverlay;

internal static class Win32Window
{
    private const int SW_RESTORE = 9;
    private const int SW_MINIMIZE = 6;

    // Virtual key codes for Alt key simulation
    private const byte VK_MENU = 0x12; // Alt key
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

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

            // Simulate Alt key press/release to give calling process "last input" privilege.
            // Windows restricts SetForegroundWindow to only work when the calling process
            // has received the last input event. Since XInput goes directly to the game,
            // our overlay process doesn't have this privilege. Simulating Alt key bypasses this.
            keybd_event(VK_MENU, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
            keybd_event(VK_MENU, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);

            // Bring to foreground
            SetForegroundWindow(hWnd);
        }
        catch
        {
            // ignore
        }
    }
}
