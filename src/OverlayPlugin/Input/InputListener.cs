using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Playnite.SDK;
using PlayniteOverlay;

namespace PlayniteOverlay.Input;

internal sealed class InputListener
{
    private const int PollIntervalMs = 100;
    private const int HotkeyRetryLimit = 10;
    private const int ToggleCooldownMs = 300;

    private static readonly ILogger logger = LogManager.GetLogger();
    private readonly ushort[] lastButtons = new ushort[4];
    private readonly bool[] controllerConnected = new bool[4];

    private Timer? pollTimer;
    private HotkeyManager? hotkeyManager;
    private DispatcherTimer? hotkeyRetryTimer;
    private string controllerCombo = "Guide";
    private string? customHotkeyGesture;
    private bool enableController = true;

    // Overlay window reference for navigation (single polling loop)
    private OverlayWindow? overlayWindow;
    private readonly object overlayWindowLock = new object();

    // Toggle cooldown tracking
    private DateTime lastToggleTime = DateTime.MinValue;

    // Track which navigation buttons have been consumed (waiting for release before re-triggering)
    private ushort consumedNavigationButtons = 0;

    public event EventHandler? ToggleRequested;

    /// <summary>
    /// Sets the overlay window reference for controller navigation.
    /// When set to non-null, navigation inputs are routed to the window.
    /// When set to null, navigation is disabled.
    /// </summary>
    public void SetOverlayWindow(OverlayWindow? window)
    {
        lock (overlayWindowLock)
        {
            overlayWindow = window;
            if (window != null)
            {
                // Capture current button state to prevent ghost inputs
                // Any buttons currently pressed won't trigger actions until released
                for (int i = 0; i < lastButtons.Length; i++)
                {
                    if (XInput.TryGetState(i, out var state))
                    {
                        lastButtons[i] = state.Gamepad.wButtons;
                    }
                }
                // Mark all currently pressed navigation buttons as consumed
                // so they don't trigger until released and pressed again
                consumedNavigationButtons = GetCurrentNavigationButtons();
            }
            else
            {
                // Reset consumed buttons when overlay closes
                consumedNavigationButtons = 0;
            }
        }
    }

    private ushort GetCurrentNavigationButtons()
    {
        ushort buttons = 0;
        for (int i = 0; i < lastButtons.Length; i++)
        {
            buttons |= lastButtons[i];
        }
        return (ushort)(buttons & NavigationButtonsMask);
    }

    // All navigation-related buttons that use consumed-button debouncing
    private const ushort NavigationButtonsMask =
        XInput.XINPUT_GAMEPAD_DPAD_UP |
        XInput.XINPUT_GAMEPAD_DPAD_DOWN |
        XInput.XINPUT_GAMEPAD_DPAD_LEFT |
        XInput.XINPUT_GAMEPAD_DPAD_RIGHT |
        XInput.XINPUT_GAMEPAD_A |
        XInput.XINPUT_GAMEPAD_B |
        XInput.XINPUT_GAMEPAD_BACK;

    /// <summary>
    /// Starts both hotkey and controller input listening.
    /// </summary>
    public void Start()
    {
        StartHotkey();
        StartController();
    }

    /// <summary>
    /// Stops both hotkey and controller input listening.
    /// </summary>
    public void Stop()
    {
        StopHotkey();
        StopController();
    }

    /// <summary>
    /// Starts only hotkey input listening (keyboard shortcut).
    /// </summary>
    public void StartHotkey()
    {
        TryRegisterHotkey();
    }

    /// <summary>
    /// Stops only hotkey input listening (keyboard shortcut).
    /// </summary>
    public void StopHotkey()
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            hotkeyRetryTimer?.Stop();
            hotkeyRetryTimer = null;
            hotkeyManager?.Unregister();
        });
    }

    /// <summary>
    /// Starts only controller input polling (Xbox controller).
    /// </summary>
    public void StartController()
    {
        pollTimer ??= new Timer(_ => PollControllers(), null, 0, PollIntervalMs);
    }

    /// <summary>
    /// Stops only controller input polling (Xbox controller).
    /// </summary>
    public void StopController()
    {
        pollTimer?.Dispose();
        pollTimer = null;
    }

    public void ApplySettings(OverlaySettings settings)
    {
        customHotkeyGesture = settings.EnableCustomHotkey ? settings.CustomHotkey : null;
        enableController = settings.UseControllerToOpen;
        controllerCombo = string.IsNullOrWhiteSpace(settings.ControllerCombo) ? "Guide" : settings.ControllerCombo;

        TryRegisterHotkey();
    }

    public void TriggerToggle()
    {
        ToggleRequested?.Invoke(this, EventArgs.Empty);
    }

    private void PollControllers()
    {
        if (!enableController)
        {
            // Controller polling is running but disabled in settings
            return;
        }

        bool useGuide = controllerCombo.Equals("Guide", StringComparison.OrdinalIgnoreCase);

        // Get current overlay window reference
        OverlayWindow? currentOverlay;
        lock (overlayWindowLock)
        {
            currentOverlay = overlayWindow;
        }

        for (int index = 0; index < lastButtons.Length; index++)
        {
            // Use TryGetStateEx when checking for Guide button (includes Guide in wButtons)
            // Use TryGetState for other combos (standard API)
            bool connected = useGuide
                ? XInput.TryGetStateEx(index, out var state)
                : XInput.TryGetState(index, out state);

            if (!connected)
            {
                if (controllerConnected[index])
                {
                    logger.Debug($"Controller {index} disconnected");
                    controllerConnected[index] = false;
                }
                lastButtons[index] = 0;
                continue;
            }

            if (!controllerConnected[index])
            {
                logger.Debug($"Controller {index} connected");
                controllerConnected[index] = true;
            }

            var buttons = state.Gamepad.wButtons;
            var previous = lastButtons[index];

            // Handle toggle combo with cooldown
            var mask = ResolveComboMask(controllerCombo);
            if (mask != 0)
            {
                bool now = (buttons & mask) == mask;
                bool prev = (previous & mask) == mask;
                if (now && !prev)
                {
                    var elapsed = (DateTime.Now - lastToggleTime).TotalMilliseconds;
                    if (elapsed >= ToggleCooldownMs)
                    {
                        logger.Debug($"Controller combo '{controllerCombo}' pressed on controller {index}");
                        lastToggleTime = DateTime.Now;
                        TriggerToggle();
                    }
                }
            }

            // Handle navigation if overlay is open
            if (currentOverlay != null)
            {
                HandleNavigation(currentOverlay, buttons);
            }

            lastButtons[index] = buttons;
        }
    }

    private void HandleNavigation(OverlayWindow window, ushort buttons)
    {
        // Clear consumed flag for any navigation buttons that are now released
        ushort releasedButtons = (ushort)(consumedNavigationButtons & ~buttons);
        consumedNavigationButtons = (ushort)(consumedNavigationButtons & ~releasedButtons);

        // Check D-pad directions - only trigger if pressed AND not consumed
        if (IsNewPress(buttons, XInput.XINPUT_GAMEPAD_DPAD_UP))
        {
            consumedNavigationButtons |= XInput.XINPUT_GAMEPAD_DPAD_UP;
            Dispatch(window, () => window.ControllerNavigateUp());
        }
        else if (IsNewPress(buttons, XInput.XINPUT_GAMEPAD_DPAD_DOWN))
        {
            consumedNavigationButtons |= XInput.XINPUT_GAMEPAD_DPAD_DOWN;
            Dispatch(window, () => window.ControllerNavigateDown());
        }
        else if (IsNewPress(buttons, XInput.XINPUT_GAMEPAD_DPAD_LEFT))
        {
            consumedNavigationButtons |= XInput.XINPUT_GAMEPAD_DPAD_LEFT;
            Dispatch(window, () => window.ControllerNavigateLeft());
        }
        else if (IsNewPress(buttons, XInput.XINPUT_GAMEPAD_DPAD_RIGHT))
        {
            consumedNavigationButtons |= XInput.XINPUT_GAMEPAD_DPAD_RIGHT;
            Dispatch(window, () => window.ControllerNavigateRight());
        }

        // Check action buttons (A, B, Back)
        if (IsNewPress(buttons, XInput.XINPUT_GAMEPAD_A))
        {
            consumedNavigationButtons |= XInput.XINPUT_GAMEPAD_A;
            Dispatch(window, () => window.ControllerAccept());
        }
        else if (IsNewPress(buttons, XInput.XINPUT_GAMEPAD_B))
        {
            consumedNavigationButtons |= XInput.XINPUT_GAMEPAD_B;
            Dispatch(window, () => window.ControllerCancel());
        }
        else if (IsNewPress(buttons, XInput.XINPUT_GAMEPAD_BACK))
        {
            consumedNavigationButtons |= XInput.XINPUT_GAMEPAD_BACK;
            Dispatch(window, () => window.ControllerCancel());
        }
    }

    /// <summary>
    /// Returns true if the button is currently pressed AND has not been consumed yet.
    /// A button is consumed when it triggers an action, and is released when the button is no longer pressed.
    /// </summary>
    private bool IsNewPress(ushort buttons, ushort mask)
    {
        return (buttons & mask) != 0 && (consumedNavigationButtons & mask) == 0;
    }

    private static void Dispatch(OverlayWindow window, Action action)
    {
        var dispatcher = window.Dispatcher;
        if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
            return;
        }

        if (dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.BeginInvoke(action, DispatcherPriority.Send);
        }
    }

    private static ushort ResolveComboMask(string combo)
    {
        var upper = combo.ToUpperInvariant();
        return upper switch
        {
            "GUIDE" => XInput.XINPUT_GAMEPAD_GUIDE,
            "START+BACK" or "BACK+START" => (ushort)(XInput.XINPUT_GAMEPAD_START | XInput.XINPUT_GAMEPAD_BACK),
            "LB+RB" or "RB+LB" => (ushort)(XInput.XINPUT_GAMEPAD_LEFT_SHOULDER | XInput.XINPUT_GAMEPAD_RIGHT_SHOULDER),
            _ => 0
        };
    }

    private void TryRegisterHotkey()
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            hotkeyRetryTimer?.Stop();
            hotkeyRetryTimer = null;

            hotkeyManager ??= new HotkeyManager();

            if (string.IsNullOrWhiteSpace(customHotkeyGesture))
            {
                hotkeyManager.Unregister();
                return;
            }

            if (hotkeyManager.Register(customHotkeyGesture!, TriggerToggle))
            {
                logger.Debug($"Successfully registered hotkey: {customHotkeyGesture}");
                return;
            }

            logger.Debug($"Failed to register hotkey immediately, will retry: {customHotkeyGesture}");
            int attempts = 0;
            hotkeyRetryTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            hotkeyRetryTimer.Tick += (_, _) =>
            {
                attempts++;
                if (hotkeyManager.Register(customHotkeyGesture!, TriggerToggle))
                {
                    logger.Debug($"Successfully registered hotkey after {attempts} attempts: {customHotkeyGesture}");
                    hotkeyRetryTimer?.Stop();
                    hotkeyRetryTimer = null;
                }
                else if (attempts >= HotkeyRetryLimit)
                {
                    logger.Warn($"Failed to register hotkey after {attempts} attempts: {customHotkeyGesture}");
                    hotkeyRetryTimer?.Stop();
                    hotkeyRetryTimer = null;
                }
            };
            hotkeyRetryTimer.Start();
        });
    }
}
