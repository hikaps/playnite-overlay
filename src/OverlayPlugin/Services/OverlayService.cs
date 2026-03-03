using System;
using System.Collections.Generic;
using System.Windows;
using Playnite.SDK;
using PlayniteOverlay.Input;
using PlayniteOverlay.Models;

namespace PlayniteOverlay.Services;

internal sealed class OverlayService
{
    private static readonly ILogger logger = LogManager.GetLogger();
    private readonly object stateLock = new object();
    private readonly object windowLock = new object();
    private readonly InputListener inputListener;
    private OverlayWindow? window;
    private int? suspendedProcessId;
    private IntPtr minimizedWindowHandle;
    private IPlayniteAPI? playniteApi;

    public OverlayService(InputListener inputListener)
    {
        this.inputListener = inputListener ?? throw new ArgumentNullException(nameof(inputListener));
    }

    /// <summary>
    /// Sets the Playnite API reference for showing notifications.
    /// Must be called before Show() if notifications are desired.
    /// </summary>
    public void SetPlayniteAPI(IPlayniteAPI api)
    {
        playniteApi = api;
    }

    public bool IsVisible
    {
        get { lock (windowLock) return window != null; }
    }

    public void Show(Action onSwitch, Action onExit, OverlayItem? currentGame, IEnumerable<RunningApp> runningApps, IEnumerable<OverlayItem> recentGames, IEnumerable<AudioDevice>? audioDevices = null, Action<string, Action<bool>>? onAudioDeviceChanged = null, GameVolumeService? gameVolumeService = null, int? currentGameProcessId = null, GameSwitcher? gameSwitcher = null, OverlaySettings? settings = null, IEnumerable<Models.OverlayShortcut>? shortcuts = null, bool suspendGame = false, bool minimizeGame = false, IntPtr gameWindowHandle = default)
    {
        lock (windowLock)
        {
            if (window != null)
            {
                return;
            }

            // Capture state atomically inside the lock
            IntPtr handleToMinimize = IntPtr.Zero;
            int? processToSuspend = null;
            
            if (minimizeGame && gameWindowHandle != IntPtr.Zero)
            {
                handleToMinimize = gameWindowHandle;
                lock (stateLock)
                {
                    minimizedWindowHandle = gameWindowHandle;
                }
                logger.Info($"Minimizing game window {gameWindowHandle}");
                Win32Window.MinimizeWindow(gameWindowHandle);
            }
            else if (minimizeGame)
            {
                logger.Warn($"Cannot minimize: minimizeGame={minimizeGame}, gameWindowHandle={gameWindowHandle}");
            }
            
            if (suspendGame && currentGameProcessId.HasValue && currentGameProcessId.Value > 0)
            {
                processToSuspend = currentGameProcessId.Value;
            }
            else if (suspendGame)
            {
                logger.Warn($"Cannot suspend: suspendGame={suspendGame}, hasProcessId={currentGameProcessId.HasValue}, processId={currentGameProcessId}");
            }

            Application.Current?.Dispatcher.Invoke(() =>
            {
                window = new OverlayWindow(onSwitch, onExit, currentGame, runningApps, recentGames, audioDevices, onAudioDeviceChanged, gameVolumeService, currentGameProcessId, gameSwitcher, shortcuts: shortcuts);

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

                    // Suspend game process if requested (happens after window is loaded)
                    if (processToSuspend.HasValue)
                    {
                        logger.Info($"Suspending game process {processToSuspend.Value} for overlay");
                        if (ProcessSuspender.SuspendProcess(processToSuspend.Value))
                        {
                            lock (stateLock)
                            {
                                suspendedProcessId = processToSuspend.Value;
                            }
                            logger.Info("Game process suspended successfully");
                        }
                        else
                        {
                            logger.Warn("Failed to suspend game process");
                        }
                    }
                };

                window.Closed += (_, _) =>
                {
                    // Disconnect controller navigation
                    inputListener.SetOverlayWindow(null);

                    // Restore the minimized game window
                    RestoreMinimizedWindow();

                    // Resume the game process if it was suspended
                    ResumeSuspendedProcess();

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

    /// <summary>
    /// Restores the minimized game window, if any.
    /// </summary>
    private void RestoreMinimizedWindow()
    {
        IntPtr hwndToRestore;
        lock (stateLock)
        {
            if (minimizedWindowHandle == IntPtr.Zero)
            {
                return;
            }
            hwndToRestore = minimizedWindowHandle;
            minimizedWindowHandle = IntPtr.Zero;
        }
        
        logger.Info($"Restoring game window {hwndToRestore}");
        Win32Window.RestoreAndActivate(hwndToRestore);
    }

    /// <summary>
    /// Resumes the suspended game process, if any.
    /// </summary>
    private void ResumeSuspendedProcess()
    {
        int? pidToResume;
        lock (stateLock)
        {
            if (!suspendedProcessId.HasValue)
            {
                return;
            }
            pidToResume = suspendedProcessId.Value;
            suspendedProcessId = null;
        }
        
        logger.Info($"Resuming game process {pidToResume.Value}");
        if (ProcessSuspender.SafeResumeProcess(pidToResume.Value))
        {
            logger.Info("Game process resumed successfully");
        }
        else
        {
            logger.Warn("Failed to resume game process");
            ShowNotification("Failed to resume game process. The game may remain frozen.", "Playnite Overlay Error");
        }
    }

    /// <summary>
    /// Shows a notification to the user if Playnite API is available.
    /// </summary>
    private void ShowNotification(string message, string title)
    {
        try
        {
            playniteApi?.Notifications?.Add(
                Guid.NewGuid().ToString(),
                $"{title}: {message}",
                NotificationType.Error
            );
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to show notification");
        }
    }
}

