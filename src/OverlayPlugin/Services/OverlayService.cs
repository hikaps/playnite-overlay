using System;
using System.Collections.Generic;
using System.Windows;
using Playnite.SDK;
using PlayniteOverlay;
using PlayniteOverlay.Models;

namespace PlayniteOverlay.Services;

internal sealed class OverlayService
{
    private static readonly ILogger logger = LogManager.GetLogger();
    private readonly object windowLock = new object();
    private OverlayWindow? window;

    public bool IsVisible
    {
        get { lock (windowLock) return window != null; }
    }

    public void Show(
        Action onSwitch,
        Action onExit,
        OverlayItem? currentGame,
        IEnumerable<RunningApp> runningApps,
        IEnumerable<OverlayItem> recentGames,
        bool enableControllerNavigation,
        int? processIdToSuspend,
        bool suspendProcess)
    {
        lock (windowLock)
        {
            if (window != null)
            {
                return;
            }

            Application.Current?.Dispatcher.Invoke(() =>
            {
                window = new OverlayWindow(
                    onSwitch,
                    onExit,
                    currentGame,
                    runningApps,
                    recentGames,
                    enableControllerNavigation,
                    processIdToSuspend,
                    suspendProcess);

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
    }
}
