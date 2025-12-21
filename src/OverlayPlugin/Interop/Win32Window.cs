using System;
using System.Runtime.InteropServices;

namespace PlayniteOverlay;

/// <summary>
/// Provides reliable window activation using AttachThreadInput pattern.
/// This allows stealing focus from other applications without requiring
/// hacky workarounds like simulating Alt key presses.
/// </summary>
internal static class Win32Window
{
    private const int SW_MINIMIZE = 6;
    private const int SW_RESTORE = 9;
    private const int SW_SHOW = 5;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int processId);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    /// <summary>
    /// Gets the current foreground window handle.
    /// </summary>
    public static IntPtr GetCurrentForegroundWindow()
    {
        return GetForegroundWindow();
    }

    /// <summary>
    /// Minimizes the specified window.
    /// </summary>
    public static void MinimizeWindow(IntPtr hWnd)
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
            // ignore - some windows may not respond to minimize
        }
    }

    /// <summary>
    /// Switches from the current foreground window to the target window.
    /// Minimizes the foreground window first to ensure fullscreen apps don't
    /// visually cover the target window.
    /// </summary>
    /// <param name="targetWindow">The window to switch to</param>
    /// <param name="minimizeCurrent">Whether to minimize the current foreground window</param>
    public static void SwitchToWindow(IntPtr targetWindow, bool minimizeCurrent = true)
    {
        if (targetWindow == IntPtr.Zero)
        {
            return;
        }

        try
        {
            IntPtr foreground = GetForegroundWindow();

            // Minimize the current foreground window if requested and it's not the target
            if (minimizeCurrent && foreground != IntPtr.Zero && foreground != targetWindow)
            {
                MinimizeWindow(foreground);
            }

            // Now activate the target window
            RestoreAndActivate(targetWindow);
        }
        catch
        {
            // Fallback to basic activation
            try
            {
                RestoreAndActivate(targetWindow);
            }
            catch
            {
                // ignore
            }
        }
    }

    /// <summary>
    /// Restores (if minimized) and activates the specified window.
    /// Uses AttachThreadInput pattern for reliable focus stealing.
    /// </summary>
    public static void RestoreAndActivate(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            return;
        }

        try
        {
            // Only restore if actually minimized - don't un-maximize maximized windows
            if (IsIconic(hWnd))
            {
                ShowWindow(hWnd, SW_RESTORE);
            }
            else
            {
                // Ensure window is visible (handles tray windows)
                ShowWindow(hWnd, SW_SHOW);
            }

            // Use AttachThreadInput pattern for reliable focus stealing
            ActivateWindowReliably(hWnd);
        }
        catch
        {
            // Fallback to basic activation
            try
            {
                SetForegroundWindow(hWnd);
            }
            catch
            {
                // ignore
            }
        }
    }

    /// <summary>
    /// Activates the overlay window reliably by stealing focus from the
    /// current foreground window (typically a game). This uses the
    /// AttachThreadInput pattern to bypass Windows focus restrictions.
    /// </summary>
    /// <param name="overlayHwnd">Handle to the overlay window</param>
    public static void ActivateOverlayWindow(IntPtr overlayHwnd)
    {
        if (overlayHwnd == IntPtr.Zero)
        {
            return;
        }

        try
        {
            ActivateWindowReliably(overlayHwnd);
        }
        catch
        {
            // Fallback to basic activation
            try
            {
                SetForegroundWindow(overlayHwnd);
            }
            catch
            {
                // ignore
            }
        }
    }

    /// <summary>
    /// Activates a window reliably by attaching to the foreground thread.
    /// Windows restricts SetForegroundWindow unless:
    /// 1. The calling process is the foreground process, OR
    /// 2. The calling thread is attached to the foreground thread
    /// 
    /// This method temporarily attaches our thread to the foreground window's
    /// thread, which allows SetForegroundWindow to succeed.
    /// </summary>
    private static void ActivateWindowReliably(IntPtr targetWindow)
    {
        IntPtr foregroundWindow = GetForegroundWindow();
        
        // If target is already foreground, nothing to do
        if (foregroundWindow == targetWindow)
        {
            return;
        }

        uint currentThreadId = GetCurrentThreadId();
        uint foregroundThreadId = GetWindowThreadProcessId(foregroundWindow, out _);
        uint targetThreadId = GetWindowThreadProcessId(targetWindow, out _);

        bool attachedToForeground = false;
        bool attachedToTarget = false;

        try
        {
            // Attach our thread to the foreground window's thread
            // This gives us permission to call SetForegroundWindow
            if (foregroundThreadId != currentThreadId)
            {
                attachedToForeground = AttachThreadInput(currentThreadId, foregroundThreadId, true);
            }

            // Also attach to the target window's thread if different
            if (targetThreadId != currentThreadId && targetThreadId != foregroundThreadId)
            {
                attachedToTarget = AttachThreadInput(currentThreadId, targetThreadId, true);
            }

            // Now we can reliably bring the window to foreground
            BringWindowToTop(targetWindow);
            SetForegroundWindow(targetWindow);
        }
        finally
        {
            // Always detach threads to avoid leaving them in a weird state
            if (attachedToForeground)
            {
                AttachThreadInput(currentThreadId, foregroundThreadId, false);
            }
            if (attachedToTarget)
            {
                AttachThreadInput(currentThreadId, targetThreadId, false);
            }
        }
    }
}
