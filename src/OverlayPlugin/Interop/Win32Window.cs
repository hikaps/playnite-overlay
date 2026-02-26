using System;
using System.Runtime.InteropServices;

namespace PlayniteOverlay;

/// <summary>
/// Provides reliable window activation using multiple focus-stealing techniques.
/// Uses a cascading fallback approach: AttachThreadInput → Alt key simulation →
/// Foreground lock bypass to ensure focus is acquired from fullscreen games.
/// </summary>
internal static class Win32Window
{
    private const int SW_MINIMIZE = 6;
    private const int SW_RESTORE = 9;
    private const int SW_SHOW = 5;

    // Virtual key codes
    private const int VK_MENU = 0x12; // Alt key
    private const int KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const int KEYEVENTF_KEYUP = 0x0002;

    // SystemParametersInfo constants
    private const int SPI_GETFOREGROUNDLOCKTIMEOUT = 0x2000;
    private const int SPI_SETFOREGROUNDLOCKTIMEOUT = 0x2001;
    private const uint SPIF_SENDCHANGE = 0x0002;

    // LockSetForegroundWindow constants
    private const uint LSFW_UNLOCK = 2;

    // AllowSetForegroundWindow constant
    private const int ASFW_ANY = -1;

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

    // Additional P/Invoke for focus stealing techniques
    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, int dwFlags, int dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);

    [DllImport("user32.dll")]
    private static extern bool LockSetForegroundWindow(uint uLockCode);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref uint pvParam, uint fWinIni);

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

            // Use cascading fallback techniques for reliable focus stealing
            ActivateWindowWithFallbacks(hWnd);
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
    /// current foreground window (typically a game). Uses a cascading
    /// fallback approach with multiple focus-stealing techniques.
    /// </summary>
    /// <param name="overlayHwnd">Handle to the overlay window</param>
    public static void ActivateOverlayWindow(IntPtr overlayHwnd)
    {
        if (overlayHwnd == IntPtr.Zero)
        {
            return;
        }

        ActivateWindowWithFallbacks(overlayHwnd);
    }

    /// <summary>
    /// Activates a window using multiple techniques in a cascading fallback approach.
    /// This is the most robust way to steal focus from fullscreen games.
    /// </summary>
    private static void ActivateWindowWithFallbacks(IntPtr targetWindow)
    {
        IntPtr foregroundWindow = GetForegroundWindow();

        // If target is already foreground, nothing to do
        if (foregroundWindow == targetWindow)
        {
            return;
        }

        // Technique 1: Try AttachThreadInput (most reliable, least invasive)
        if (TryAttachThreadInput(targetWindow, foregroundWindow))
        {
            return;
        }

        // Technique 2: Alt key simulation (tricks Windows into unlocking focus)
        if (TryAltKeySimulation(targetWindow))
        {
            return;
        }

        // Technique 3: Foreground lock timeout bypass (most invasive, last resort)
        TryForegroundLockBypass(targetWindow);
    }

    /// <summary>
    /// Technique 1: AttachThreadInput pattern.
    /// Temporarily attaches our thread to the foreground thread's input queue,
    /// which grants permission to call SetForegroundWindow.
    /// </summary>
    /// <returns>True if focus was successfully acquired</returns>
    private static bool TryAttachThreadInput(IntPtr targetWindow, IntPtr foregroundWindow)
    {
        uint currentThreadId = GetCurrentThreadId();
        uint foregroundThreadId = GetWindowThreadProcessId(foregroundWindow, out _);
        uint targetThreadId = GetWindowThreadProcessId(targetWindow, out _);

        bool attachedToForeground = false;
        bool attachedToTarget = false;

        try
        {
            // Attach our thread to the foreground window's thread
            // This gives us permission to call SetForegroundWindow
            if (foregroundThreadId != currentThreadId && foregroundThreadId != 0)
            {
                attachedToForeground = AttachThreadInput(currentThreadId, foregroundThreadId, true);
            }

            // Also attach to the target window's thread if different
            if (targetThreadId != currentThreadId && targetThreadId != foregroundThreadId && targetThreadId != 0)
            {
                attachedToTarget = AttachThreadInput(currentThreadId, targetThreadId, true);
            }

            // Now we can reliably bring the window to foreground
            BringWindowToTop(targetWindow);
            SetForegroundWindow(targetWindow);

            // Verify success
            return GetForegroundWindow() == targetWindow;
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

    /// <summary>
    /// Technique 2: Alt key simulation.
    /// Windows unlocks SetForegroundWindow when the user presses Alt.
    /// Simulating an Alt keypress tricks the system into allowing focus changes.
    /// </summary>
    /// <returns>True if focus was successfully acquired</returns>
    private static bool TryAltKeySimulation(IntPtr targetWindow)
    {
        bool simulatedAlt = false;

        try
        {
            // Only simulate Alt if it's not already pressed
            if ((GetAsyncKeyState(VK_MENU) & 0x8000) == 0)
            {
                keybd_event(VK_MENU, 0, KEYEVENTF_EXTENDEDKEY, 0);
                simulatedAlt = true;
            }

            BringWindowToTop(targetWindow);
            SetForegroundWindow(targetWindow);

            // Verify success
            return GetForegroundWindow() == targetWindow;
        }
        finally
        {
            // Always release the Alt key if we pressed it
            if (simulatedAlt)
            {
                keybd_event(VK_MENU, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, 0);
            }
        }
    }

    /// <summary>
    /// Technique 3: Foreground lock timeout bypass.
    /// Temporarily disables Windows' foreground lock restrictions,
    /// allowing any process to set the foreground window.
    /// This is the most invasive technique and should be used as a last resort.
    /// </summary>
    /// <returns>True if focus was successfully acquired</returns>
    private static bool TryForegroundLockBypass(IntPtr targetWindow)
    {
        uint oldTimeout = 0;

        try
        {
            // Save the current foreground lock timeout
            SystemParametersInfo(SPI_GETFOREGROUNDLOCKTIMEOUT, 0, ref oldTimeout, 0);

            // Disable the foreground lock timeout
            uint newTimeout = 0;
            SystemParametersInfo(SPI_SETFOREGROUNDLOCKTIMEOUT, 0, ref newTimeout, SPIF_SENDCHANGE);

            // Unlock foreground window changes
            LockSetForegroundWindow(LSFW_UNLOCK);

            // Allow any process to set foreground window
            AllowSetForegroundWindow(ASFW_ANY);

            // Now try to set the foreground window
            BringWindowToTop(targetWindow);
            SetForegroundWindow(targetWindow);

            return GetForegroundWindow() == targetWindow;
        }
        finally
        {
            // Restore the original foreground lock timeout
            SystemParametersInfo(SPI_SETFOREGROUNDLOCKTIMEOUT, 0, ref oldTimeout, SPIF_SENDCHANGE);
        }
    }
}
