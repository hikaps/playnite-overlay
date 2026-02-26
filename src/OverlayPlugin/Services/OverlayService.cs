using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
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
    private int? suspendedProcessId;
    private bool suspendEnabled;
    private IntPtr minimizedWindowHandle;

    public OverlayService(InputListener inputListener)
    {
        this.inputListener = inputListener ?? throw new ArgumentNullException(nameof(inputListener));
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

            // Minimize the game window if enabled
            if (minimizeGame && gameWindowHandle != IntPtr.Zero)
            {
                logger.Info($"Minimizing game window {gameWindowHandle}");
                minimizedWindowHandle = gameWindowHandle;
                ShowWindow(gameWindowHandle, SW_MINIMIZE);
            }

            // Suspend the game process before showing overlay if enabled
            suspendEnabled = suspendGame;
            logger.Debug($"Suspend setting enabled: {suspendGame}, ProcessId: {currentGameProcessId}");
            
            if (suspendGame && currentGameProcessId.HasValue && currentGameProcessId.Value > 0)
            {
                logger.Info($"Suspending game process {currentGameProcessId.Value} for overlay");
                if (ProcessSuspender.SuspendProcess(currentGameProcessId.Value))
                {
                    suspendedProcessId = currentGameProcessId.Value;
                    logger.Info("Game process suspended successfully");
                }
                else
                {
                    logger.Warn("Failed to suspend game process");
                }
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
        if (minimizedWindowHandle != IntPtr.Zero)
        {
            var hwnd = minimizedWindowHandle;
            minimizedWindowHandle = IntPtr.Zero;
            
            logger.Info($"Restoring game window {hwnd}");
            ShowWindow(hwnd, SW_RESTORE);
            SetForegroundWindow(hwnd);
        }
    }

    /// <summary>
    /// Resumes the suspended game process, if any.
    /// </summary>
    private void ResumeSuspendedProcess()
    {
        if (suspendedProcessId.HasValue)
        {
            var pid = suspendedProcessId.Value;
            suspendedProcessId = null;
            
            logger.Info($"Resuming game process {pid}");
            if (ProcessSuspender.SafeResumeProcess(pid))
            {
                logger.Info("Game process resumed successfully");
            }
            else
            {
                logger.Warn("Failed to resume game process");
            }
        }
    }

    #region P/Invoke for window management

    private const int SW_MINIMIZE = 6;
    private const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    #endregion
}
