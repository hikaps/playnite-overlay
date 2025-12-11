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

    public void Show(Action onSwitch, Action onExit, string title, IEnumerable<OverlayItem> items, bool enableControllerNavigation)
    {
        lock (windowLock)
        {
            if (window != null)
            {
                return;
            }

            Application.Current?.Dispatcher.Invoke(() =>
            {
                window = new OverlayWindow(onSwitch, onExit, title, items, enableControllerNavigation)
                {
                    Topmost = true
                };

                window.Loaded += (_, _) =>
                {
                    var pixelBounds = Monitors.GetActiveMonitorBoundsInPixels();
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
