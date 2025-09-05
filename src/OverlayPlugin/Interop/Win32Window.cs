using System;
using System.Runtime.InteropServices;

namespace PlayniteOverlay;

internal static class Win32Window
{
    private const int SW_SHOW = 5;
    private const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    public static IntPtr GetAnyWindowForProcess(int processId)
    {
        IntPtr found = IntPtr.Zero;
        try
        {
            EnumWindows((h, p) =>
            {
                if (found != IntPtr.Zero)
                {
                    return false;
                }
                GetWindowThreadProcessId(h, out var pid);
                if (pid == (uint)processId)
                {
                    found = h;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
        }
        catch { }
        return found;
    }

    public static IntPtr GetVisibleWindowForProcess(int processId)
    {
        IntPtr found = IntPtr.Zero;
        try
        {
            EnumWindows((h, p) =>
            {
                if (found != IntPtr.Zero)
                {
                    return false;
                }
                if (!IsWindowVisible(h))
                {
                    return true;
                }
                GetWindowThreadProcessId(h, out var pid);
                if (pid == (uint)processId)
                {
                    found = h;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
        }
        catch { }
        return found;
    }

    public static IntPtr GetForeground() => GetForegroundWindow();

    public static bool RestoreAndActivate(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            // Ensure window is shown
            ShowWindow(hWnd, SW_SHOW);

            // Restore if minimized
            if (IsIconic(hWnd))
            {
                ShowWindow(hWnd, SW_RESTORE);
            }

            // Bring to foreground
            return SetForegroundWindow(hWnd);
        }
        catch
        {
            // ignore
            return false;
        }
    }
}
