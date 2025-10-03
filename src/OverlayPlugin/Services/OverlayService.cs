using System;
using System.Collections.Generic;
using System.Windows;
using PlayniteOverlay;
using PlayniteOverlay.Models;

namespace PlayniteOverlay.Services;

internal sealed class OverlayService
{
    private OverlayWindow? window;

    public bool IsVisible => window != null;

    public void Show(Action onSwitch, Action onExit, string title, IEnumerable<OverlayItem> items)
    {
        if (IsVisible)
        {
            return;
        }

        Application.Current?.Dispatcher.Invoke(() =>
        {
            if (window != null)
            {
                return;
            }

            window = new OverlayWindow(onSwitch, onExit, title, items)
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

            window.Closed += (_, _) => window = null;
            window.Show();
        });
    }

    public void Hide()
    {
        if (!IsVisible)
        {
            return;
        }

        Application.Current?.Dispatcher.Invoke(() =>
        {
            try
            {
                window?.Close();
            }
            catch
            {
                // Best effort close; swallow exceptions so overlay shutdown never crashes the plugin.
            }
            finally
            {
                window = null;
            }
        });
    }
}
