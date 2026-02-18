using System;
using System.Collections.Generic;
using System.Windows;
using Playnite.SDK;
using PlayniteOverlay;
using PlayniteOverlay.Input;
using PlayniteOverlay.Models;

namespace PlayniteOverlay.Services;

internal sealed class OverlayService
{
    private static readonly ILogger logger = LogManager.GetLogger();
    private readonly object windowLock = new object();
    private readonly InputListener inputListener;
    private OverlayWindow? window;

    public OverlayService(InputListener inputListener)
    {
        this.inputListener = inputListener ?? throw new ArgumentNullException(nameof(inputListener));
    }

    public bool IsVisible
    {
        get { lock (windowLock) return window != null; }
    }

    public void Show(Action onSwitch, Action onExit, OverlayItem? currentGame, IEnumerable<RunningApp> runningApps, IEnumerable<OverlayItem> recentGames, IEnumerable<AudioDevice>? audioDevices = null, Action<string, Action<bool>>? onAudioDeviceChanged = null, GameVolumeService? gameVolumeService = null, int? currentGameProcessId = null)
    {
        lock (windowLock)
        {
            if (window != null)
            {
                return;
            }

            Application.Current?.Dispatcher.Invoke(() =>
            {
                window = new OverlayWindow(onSwitch, onExit, currentGame, runningApps, recentGames, audioDevices, onAudioDeviceChanged, gameVolumeService, currentGameProcessId);

                window.Loaded += (_, _) =>
                {
                    // Get the monitor where the foreground window (game) is displayed
                    var pixelBounds = Monitors.GetForegroundWindowMonitorBounds();
                    var dipBounds = Monitors.PixelsToDips(window, pixelBounds);
                    window.Left = dipBounds.Left;
                    window.Top = dipBounds.Top;
                    window.Width = dipBounds.Width;
                    window.Height = dipBounds.Height;

                    // Wire up controller navigation via InputListener
                    inputListener.SetOverlayWindow(window);
                };

                window.Closed += (_, _) =>
                {
                    // Disconnect controller navigation
                    inputListener.SetOverlayWindow(null);

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
