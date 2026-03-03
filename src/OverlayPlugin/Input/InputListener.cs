using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Playnite.SDK;

namespace PlayniteOverlay.Input;

internal sealed class InputListener
{
    private const int PollIntervalMs = 100;
    private const int HotkeyRetryLimit = 10;
    private const int ToggleCooldownMs = 300;

    private static readonly ILogger logger = LogManager.GetLogger();
    
    // SDL2 controller tracking
    private readonly List<LoadedController> controllers = new List<LoadedController>();
    private readonly object controllersLock = new object();

    private Timer? pollTimer;
    private HotkeyManager? hotkeyManager;
    private DispatcherTimer? hotkeyRetryTimer;
    private string controllerCombo = "Guide";
    private string? customHotkeyGesture;
    private bool enableController = true;
    private bool runtimeControllerEnabled = true; // Runtime override for game context (e.g., PC Games Only filter)

    // Overlay window reference for navigation (single polling loop)
    private OverlayWindow? overlayWindow;
    private readonly object overlayWindowLock = new object();

    // Toggle cooldown tracking
    private DateTime lastToggleTime = DateTime.MinValue;

    // Track which navigation buttons have been consumed per controller (waiting for release before re-triggering)
    private readonly Dictionary<int, HashSet<int>> consumedNavigationButtons = new Dictionary<int, HashSet<int>>();

    // Prevent duplicate navigation from multiple controllers reporting the same input in a single poll cycle
    private bool navigationHandledThisCycle = false;

    public event EventHandler? ToggleRequested;

    /// <summary>
    /// Represents a loaded SDL2 game controller.
    /// </summary>
    private class LoadedController
    {
        public IntPtr Handle { get; }
        public int InstanceId { get; }
        public string Name { get; }
        public bool Enabled { get; set; } = true;

        // Track previous button states for edge detection
        public readonly HashSet<int> PressedButtons = new HashSet<int>();

        public LoadedController(IntPtr handle, int instanceId, string name)
        {
            Handle = handle;
            InstanceId = instanceId;
            Name = name;
        }
    }

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
                lock (controllersLock)
                {
                    foreach (var controller in controllers)
                    {
                        if (controller.Enabled)
                        {
                            SDL2.GameControllerUpdate();
                            CaptureCurrentButtonState(controller);
                        }
                    }
                }
            }
            else
            {
                // Reset consumed buttons for all controllers when overlay closes
                lock (controllersLock)
                {
                    consumedNavigationButtons.Clear();
                }
            }
        }
    }

    private void CaptureCurrentButtonState(LoadedController controller)
    {
        // Mark all currently pressed navigation buttons as consumed
        // so they don't trigger until released and pressed again
        var consumed = new HashSet<int>();
        foreach (var button in NavigationButtons)
        {
            if (SDL2.GameControllerGetButton(controller.Handle, button) == 1)
            {
                consumed.Add(button);
            }
        }
        consumedNavigationButtons[controller.InstanceId] = consumed;
    }

    // All navigation-related buttons that use consumed-button debouncing
    private static readonly int[] NavigationButtons = new int[]
    {
        SDL2.SDL_CONTROLLER_BUTTON_DPAD_UP,
        SDL2.SDL_CONTROLLER_BUTTON_DPAD_DOWN,
        SDL2.SDL_CONTROLLER_BUTTON_DPAD_LEFT,
        SDL2.SDL_CONTROLLER_BUTTON_DPAD_RIGHT,
        SDL2.SDL_CONTROLLER_BUTTON_A,
        SDL2.SDL_CONTROLLER_BUTTON_B,
        SDL2.SDL_CONTROLLER_BUTTON_BACK
    };

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
    /// Starts only controller input polling.
    /// </summary>
    public void StartController()
    {
        // Initialize SDL2 if not already done
        if (!SDL2.Init())
        {
            logger.Error("Failed to initialize SDL2 for controller input");
            return;
        }

        // Scan for already-connected controllers
        ScanForControllers();

        pollTimer ??= new Timer(_ => PollControllers(), null, 0, PollIntervalMs);
    }

    /// <summary>
    /// Stops only controller input polling.
    /// </summary>
    public void StopController()
    {
        pollTimer?.Dispose();
        pollTimer = null;

        // Close all open controllers
        lock (controllersLock)
        {
            foreach (var controller in controllers)
            {
                SDL2.GameControllerClose(controller.Handle);
            }
            controllers.Clear();
            consumedNavigationButtons.Clear();
        }

        SDL2.Quit();
    }

    /// <summary>
    /// Enables controller input processing at runtime without affecting the polling timer.
    /// Used for dynamic enable/disable based on game context (e.g., PC Games Only filter).
    /// </summary>
    public void EnableControllerInput()
    {
        runtimeControllerEnabled = true;
    }

    /// <summary>
    /// Disables controller input processing at runtime without affecting the polling timer.
    /// Used for dynamic enable/disable based on game context (e.g., PC Games Only filter).
    /// </summary>
    public void DisableControllerInput()
    {
        runtimeControllerEnabled = false;
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

    private void ScanForControllers()
    {
        lock (controllersLock)
        {
            var numJoysticks = SDL2.NumJoysticks();
            logger.Debug($"ScanForControllers: Found {numJoysticks} joystick(s)");
            
            for (int i = 0; i < numJoysticks; i++)
            {
                var isController = SDL2.IsGameController(i);
                logger.Debug($"ScanForControllers: Joystick {i} - IsGameController={isController}");
                
                if (isController)
                {
                    AddController(i);
                }
            }
            
            logger.Debug($"ScanForControllers: Total controllers opened: {controllers.Count}");
        }
    }

    private void AddController(int joystickIndex)
    {
        var handle = SDL2.GameControllerOpen(joystickIndex);
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var instanceId = SDL2.GameControllerGetJoystickInstanceID(handle);
        var name = SDL2.GameControllerName(handle) ?? $"Controller {instanceId}";

        // Check if we already have this controller
        lock (controllersLock)
        {
            if (controllers.Exists(c => c.InstanceId == instanceId))
            {
                SDL2.GameControllerClose(handle);
                return;
            }

            var controller = new LoadedController(handle, instanceId, name);
            controllers.Add(controller);
            logger.Debug($"Controller connected: {name} (instance {instanceId})");
        }
    }

    private void RemoveController(int instanceId)
    {
        lock (controllersLock)
        {
            var controller = controllers.FirstOrDefault(c => c.InstanceId == instanceId);
            if (controller != null)
            {
                SDL2.GameControllerClose(controller.Handle);
                controllers.Remove(controller);
                consumedNavigationButtons.Remove(instanceId);
                logger.Debug($"Controller disconnected: {controller.Name} (instance {instanceId})");
            }
        }
    }

    private void ProcessEvents()
    {
        while (SDL2.PollEvent(out var sdlEvent))
        {
            switch (sdlEvent.type)
            {
                case SDL2.SDL_CONTROLLERDEVICEADDED:
                    // which contains the joystick index for the newly added controller
                    var addedIndex = sdlEvent.cdevice.which;
                    logger.Debug($"SDL_EVENT: Controller added (joystick index {addedIndex})");
                    if (SDL2.IsGameController(addedIndex))
                    {
                        AddController(addedIndex);
                    }
                    break;

                case SDL2.SDL_CONTROLLERDEVICEREMOVED:
                    // which contains the instance ID of the removed controller
                    var removedInstanceId = sdlEvent.cdevice.which;
                    logger.Debug($"SDL_EVENT: Controller removed (instance ID {removedInstanceId})");
                    RemoveController(removedInstanceId);
                    break;
            }
        }
    }
    private void PollControllers()
    {
        if (!enableController || !runtimeControllerEnabled)
        {
            // Controller polling is running but disabled in settings or runtime
            return;
        }

        // Reset navigation flag at start of each poll cycle
        navigationHandledThisCycle = false;

        // Process SDL events for hot-plugging
        ProcessEvents();

        // Update controller states
        SDL2.GameControllerUpdate();

        // Get current overlay window reference
        OverlayWindow? currentOverlay;
        lock (overlayWindowLock)
        {
            currentOverlay = overlayWindow;
        }

        // Process each controller
        List<LoadedController> controllersCopy;
        lock (controllersLock)
        {
            controllersCopy = new List<LoadedController>(controllers);
        }

        foreach (var controller in controllersCopy)
        {
            if (!controller.Enabled)
            {
                continue;
            }

            ProcessController(controller, currentOverlay);
        }
    }

    private void ProcessController(LoadedController controller, OverlayWindow? currentOverlay)
    {
        // Get current button states
        var currentButtons = new HashSet<int>();
        foreach (var button in AllButtons)
        {
            if (SDL2.GameControllerGetButton(controller.Handle, button) == 1)
            {
                currentButtons.Add(button);
            }
        }

        // Handle toggle combo with cooldown
        var toggleMask = ResolveComboMask(controllerCombo);
        if (toggleMask.Length > 0)
        {
            bool allPressed = true;
            foreach (var button in toggleMask)
            {
                if (!currentButtons.Contains(button))
                {
                    allPressed = false;
                    break;
                }
            }

            bool wasPressed = true;
            foreach (var button in toggleMask)
            {
                if (!controller.PressedButtons.Contains(button))
                {
                    wasPressed = false;
                    break;
                }
            }

            if (allPressed && !wasPressed)
            {
                var elapsed = (DateTime.Now - lastToggleTime).TotalMilliseconds;
                if (elapsed >= ToggleCooldownMs)
                {
                    logger.Debug($"Controller combo '{controllerCombo}' pressed on {controller.Name}");
                    lastToggleTime = DateTime.Now;
                    TriggerToggle();
                }
            }
        }

        // Handle navigation if overlay is open
        if (currentOverlay != null)
        {
            HandleNavigation(currentOverlay, controller, currentButtons);
        }

        // Update previous state
        controller.PressedButtons.Clear();
        foreach (var button in currentButtons)
        {
            controller.PressedButtons.Add(button);
        }
    }

    private void HandleNavigation(OverlayWindow window, LoadedController controller, HashSet<int> currentButtons)
    {
        // Only allow one navigation action per poll cycle (prevents duplicate input from
        // controllers that register as multiple devices)
        if (navigationHandledThisCycle)
        {
            return;
        }

        // Get or create consumed buttons set for this controller
        if (!consumedNavigationButtons.TryGetValue(controller.InstanceId, out var consumed))
        {
            consumed = new HashSet<int>();
            consumedNavigationButtons[controller.InstanceId] = consumed;
        }

        // Clear consumed flag for any navigation buttons that are now released
        consumed.RemoveWhere(button => !currentButtons.Contains(button));

        // Check D-pad directions - only trigger if pressed AND not consumed
        if (IsNewPress(currentButtons, consumed, SDL2.SDL_CONTROLLER_BUTTON_DPAD_UP))
        {
            consumed.Add(SDL2.SDL_CONTROLLER_BUTTON_DPAD_UP);
            navigationHandledThisCycle = true;
            Dispatch(window, () => window.ControllerNavigateUp());
        }
        else if (IsNewPress(currentButtons, consumed, SDL2.SDL_CONTROLLER_BUTTON_DPAD_DOWN))
        {
            consumed.Add(SDL2.SDL_CONTROLLER_BUTTON_DPAD_DOWN);
            navigationHandledThisCycle = true;
            Dispatch(window, () => window.ControllerNavigateDown());
        }
        else if (IsNewPress(currentButtons, consumed, SDL2.SDL_CONTROLLER_BUTTON_DPAD_LEFT))
        {
            consumed.Add(SDL2.SDL_CONTROLLER_BUTTON_DPAD_LEFT);
            navigationHandledThisCycle = true;
            Dispatch(window, () => window.ControllerNavigateLeft());
        }
        else if (IsNewPress(currentButtons, consumed, SDL2.SDL_CONTROLLER_BUTTON_DPAD_RIGHT))
        {
            consumed.Add(SDL2.SDL_CONTROLLER_BUTTON_DPAD_RIGHT);
            navigationHandledThisCycle = true;
            Dispatch(window, () => window.ControllerNavigateRight());
        }

        // Check action buttons (A, B, Back)
        if (IsNewPress(currentButtons, consumed, SDL2.SDL_CONTROLLER_BUTTON_A))
        {
            consumed.Add(SDL2.SDL_CONTROLLER_BUTTON_A);
            navigationHandledThisCycle = true;
            Dispatch(window, () => window.ControllerAccept());
        }
        else if (IsNewPress(currentButtons, consumed, SDL2.SDL_CONTROLLER_BUTTON_B))
        {
            consumed.Add(SDL2.SDL_CONTROLLER_BUTTON_B);
            navigationHandledThisCycle = true;
            Dispatch(window, () => window.ControllerCancel());
        }
        else if (IsNewPress(currentButtons, consumed, SDL2.SDL_CONTROLLER_BUTTON_BACK))
        {
            consumed.Add(SDL2.SDL_CONTROLLER_BUTTON_BACK);
            navigationHandledThisCycle = true;
            Dispatch(window, () => window.ControllerCancel());
        }
    }

    /// <summary>
    /// Returns true if the button is currently pressed AND has not been consumed yet.
    /// A button is consumed when it triggers an action, and is released when the button is no longer pressed.
    /// </summary>
    private static bool IsNewPress(HashSet<int> currentButtons, HashSet<int> consumed, int button)
    {
        return currentButtons.Contains(button) && !consumed.Contains(button);
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

    // All buttons we track for toggle combos
    private static readonly int[] AllButtons = new int[]
    {
        SDL2.SDL_CONTROLLER_BUTTON_A,
        SDL2.SDL_CONTROLLER_BUTTON_B,
        SDL2.SDL_CONTROLLER_BUTTON_X,
        SDL2.SDL_CONTROLLER_BUTTON_Y,
        SDL2.SDL_CONTROLLER_BUTTON_BACK,
        SDL2.SDL_CONTROLLER_BUTTON_GUIDE,
        SDL2.SDL_CONTROLLER_BUTTON_START,
        SDL2.SDL_CONTROLLER_BUTTON_LEFTSTICK,
        SDL2.SDL_CONTROLLER_BUTTON_RIGHTSTICK,
        SDL2.SDL_CONTROLLER_BUTTON_LEFTSHOULDER,
        SDL2.SDL_CONTROLLER_BUTTON_RIGHTSHOULDER,
        SDL2.SDL_CONTROLLER_BUTTON_DPAD_UP,
        SDL2.SDL_CONTROLLER_BUTTON_DPAD_DOWN,
        SDL2.SDL_CONTROLLER_BUTTON_DPAD_LEFT,
        SDL2.SDL_CONTROLLER_BUTTON_DPAD_RIGHT
    };

    private static int[] ResolveComboMask(string combo)
    {
        var upper = combo.ToUpperInvariant();
        return upper switch
        {
            "GUIDE" => new int[] { SDL2.SDL_CONTROLLER_BUTTON_GUIDE },
            "START+BACK" or "BACK+START" => new int[] { SDL2.SDL_CONTROLLER_BUTTON_START, SDL2.SDL_CONTROLLER_BUTTON_BACK },
            "LB+RB" or "RB+LB" => new int[] { SDL2.SDL_CONTROLLER_BUTTON_LEFTSHOULDER, SDL2.SDL_CONTROLLER_BUTTON_RIGHTSHOULDER },
            _ => Array.Empty<int>()
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
