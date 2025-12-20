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
    private const int NavigationCooldownMs = 150;

    private static readonly ILogger logger = LogManager.GetLogger();
    private DateTime lastToggleTime = DateTime.MinValue;
    private DateTime lastNavigationTime = DateTime.MinValue;
    private readonly ushort[] lastButtons = new ushort[4];
    private readonly bool[] controllerConnected = new bool[4];

    private Timer? pollTimer;
    private HotkeyManager? hotkeyManager;
    private DispatcherTimer? hotkeyRetryTimer;
    private string controllerCombo = "Guide";
    private string? customHotkeyGesture;
    private bool enableController = true;

    // Reference to overlay window for navigation (set when overlay opens, cleared when it closes)
    private OverlayWindow? overlayWindow;
    private readonly object overlayWindowLock = new object();

    public event EventHandler? ToggleRequested;

    /// <summary>
    /// Sets the overlay window reference for navigation input handling.
    /// Call this when the overlay opens. Pass null when the overlay closes.
    /// </summary>
    public void SetOverlayWindow(OverlayWindow? window)
    {
        lock (overlayWindowLock)
        {
            overlayWindow = window;

            if (window != null)
            {
                // Capture current button state to prevent ghost inputs
                // Any buttons pressed when overlay opens won't trigger navigation
                for (int i = 0; i < lastButtons.Length; i++)
                {
                    if (XInput.TryGetState(i, out var state))
                    {
                        lastButtons[i] = state.Gamepad.wButtons;
                    }
                }
                lastNavigationTime = DateTime.Now;
            }
        }
    }

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

        // Check if overlay is currently open
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

            // Always handle toggle combo (works whether overlay is open or closed)
            HandleToggleCombo(index, previous, buttons);

            // Handle navigation only when overlay is open
            if (currentOverlay != null)
            {
                HandleNavigation(currentOverlay, previous, buttons);
            }

            lastButtons[index] = buttons;
        }
    }

    private void HandleToggleCombo(int playerIndex, ushort previous, ushort buttons)
    {
        var mask = ResolveComboMask(controllerCombo);

        if (mask != 0)
        {
            bool now = (buttons & mask) == mask;
            bool prev = (previous & mask) == mask;
            if (now && !prev)
            {
                // Apply cooldown to prevent double-triggers from button bounce
                if ((DateTime.Now - lastToggleTime).TotalMilliseconds < ToggleCooldownMs)
                {
                    return;
                }
                lastToggleTime = DateTime.Now;
                logger.Debug($"Controller combo '{controllerCombo}' pressed on controller {playerIndex}");
                TriggerToggle();
            }
        }
    }

    private void HandleNavigation(OverlayWindow window, ushort previous, ushort buttons)
    {
        // D-pad navigation
        if (IsPressed(previous, buttons, XInput.XINPUT_GAMEPAD_DPAD_UP))
        {
            DispatchNavigationWithCooldown(() => window.ControllerNavigateUp());
        }

        if (IsPressed(previous, buttons, XInput.XINPUT_GAMEPAD_DPAD_DOWN))
        {
            DispatchNavigationWithCooldown(() => window.ControllerNavigateDown());
        }

        if (IsPressed(previous, buttons, XInput.XINPUT_GAMEPAD_DPAD_LEFT))
        {
            DispatchNavigationWithCooldown(() => window.ControllerNavigateLeft());
        }

        if (IsPressed(previous, buttons, XInput.XINPUT_GAMEPAD_DPAD_RIGHT))
        {
            DispatchNavigationWithCooldown(() => window.ControllerNavigateRight());
        }

        // Action buttons
        if (IsPressed(previous, buttons, XInput.XINPUT_GAMEPAD_A))
        {
            DispatchNavigationWithCooldown(() => window.ControllerAccept());
        }

        if (IsPressed(previous, buttons, XInput.XINPUT_GAMEPAD_B)
            || IsPressed(previous, buttons, XInput.XINPUT_GAMEPAD_BACK))
        {
            DispatchNavigationWithCooldown(() => window.ControllerCancel());
        }
    }

    private void DispatchNavigationWithCooldown(Action action)
    {
        if ((DateTime.Now - lastNavigationTime).TotalMilliseconds < NavigationCooldownMs)
        {
            return;
        }
        lastNavigationTime = DateTime.Now;
        DispatchToWindow(action);
    }

    private void DispatchToWindow(Action action)
    {
        OverlayWindow? window;
        lock (overlayWindowLock)
        {
            window = overlayWindow;
        }

        if (window == null)
        {
            return;
        }

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

    private static bool IsPressed(ushort previous, ushort current, ushort mask)
    {
        return (current & mask) != 0 && (previous & mask) == 0;
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
