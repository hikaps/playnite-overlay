using System;
using System.Collections.Generic;
using System.Windows;
using Playnite.SDK;
using PlayniteOverlay;
using PlayniteOverlay.Interop;
using PlayniteOverlay.Models;

namespace PlayniteOverlay.Services;

internal sealed class OverlayService
{
    private static readonly ILogger logger = LogManager.GetLogger();
    private readonly object windowLock = new object();
    private OverlayWindow? window;
    private int suspendedProcessId;

    /// <summary>
    /// Gets the process ID that is currently suspended, or 0 if none.
    /// </summary>
    public int SuspendedProcessId
    {
        get { lock (windowLock) return suspendedProcessId; }
    }

    public bool IsVisible
    {
        get { lock (windowLock) return window != null; }
    }

    /// <summary>
    /// Shows the overlay window.
    /// </summary>
    /// <param name="onSwitch">Action to invoke when user clicks Switch to Playnite</param>
    /// <param name="onExit">Action to invoke when user clicks Exit game</param>
    /// <param name="currentGame">Current game item to display, or null</param>
    /// <param name="runningApps">List of running apps to display</param>
    /// <param name="recentGames">List of recent games to display</param>
    /// <param name="enableControllerNavigation">Whether to enable controller navigation</param>
    /// <param name="processIdToSuspend">Process ID to suspend while overlay is active, or 0 to skip suspension</param>
    public void Show(
        Action onSwitch,
        Action onExit,
        OverlayItem? currentGame,
        IEnumerable<RunningApp> runningApps,
        IEnumerable<OverlayItem> recentGames,
        bool enableControllerNavigation,
        int processIdToSuspend = 0)
    {
        lock (windowLock)
        {
            if (window != null)
            {
                return;
            }

            // Suspend the game process if requested
            if (processIdToSuspend > 0)
            {
                if (ProcessSuspender.SuspendProcess(processIdToSuspend))
                {
                    suspendedProcessId = processIdToSuspend;
                }
            }

            Application.Current?.Dispatcher.Invoke(() =>
            {
                window = new OverlayWindow(onSwitch, onExit, currentGame, runningApps, recentGames, enableControllerNavigation);

                window.Loaded += (_, _) =>
                {
                    // Get the monitor where the foreground window (game) is displayed
                    var pixelBounds = Monitors.GetForegroundWindowMonitorBounds();
                    var dipBounds = Monitors.PixelsToDips(window, pixelBounds);
                    window.Left = dipBounds.Left;
                    window.Top = dipBounds.Top;
                    window.Width = dipBounds.Width;
                    window.Height = dipBounds.Height;
                };

                window.Closed += (_, _) =>
                {
                    lock (windowLock)
                    {
                        window = null;
                        
                        // Resume the suspended process when overlay closes
                        ResumeIfSuspended();
                    }
                };

                window.Show();
            });
        }
    }

    public void Hide()
    {
        OverlayWindow? windowToClose = null;

        lock (windowLock)
        {
            if (window == null)
            {
                return;
            }

            windowToClose = window;
            window = null;
        }

        Application.Current?.Dispatcher.Invoke(() =>
        {
            try
            {
                windowToClose?.Close();
            }
            catch (Exception ex)
            {
                logger.Debug(ex, "Exception while closing overlay window.");
            }
        });

        // Resume happens in window.Closed event, but also try here as a safety net
        lock (windowLock)
        {
            ResumeIfSuspended();
        }
    }

    /// <summary>
    /// Resumes any suspended process. Call this as a safety measure on plugin dispose.
    /// </summary>
    public void ResumeIfSuspended()
    {
        if (suspendedProcessId > 0)
        {
            ProcessSuspender.ResumeProcess(suspendedProcessId);
            suspendedProcessId = 0;
        }
    }
}
