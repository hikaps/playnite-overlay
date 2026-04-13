using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Threading;
using Playnite.SDK;
using Playnite.SDK.Events;

namespace PlayniteOverlay.Input;

internal sealed class InputListener
{
    private const int HotkeyRetryLimit = 10;
    private const int ToggleCooldownMs = 300;

    private static readonly ILogger logger = LogManager.GetLogger();

    private HotkeyManager? hotkeyManager;
    private DispatcherTimer? hotkeyRetryTimer;
    private string? customHotkeyGesture;
    private string controllerCombo = "Guide";
    private bool enableController = true;
    private bool runtimeControllerEnabled = true;

    private OverlayWindow? overlayWindow;
    private DateTime lastToggleTime = DateTime.MinValue;

    // Per-controller button state tracking
    private readonly Dictionary<int, HashSet<ControllerInput>> pressedButtons = new Dictionary<int, HashSet<ControllerInput>>();
    private readonly Dictionary<int, HashSet<ControllerInput>> consumedButtons = new Dictionary<int, HashSet<ControllerInput>>();
    private readonly Dictionary<int, bool> comboTriggered = new Dictionary<int, bool>();

    private static readonly HashSet<ControllerInput> NavigationButtons = new HashSet<ControllerInput>
    {
        ControllerInput.DPadUp, ControllerInput.DPadDown,
        ControllerInput.DPadLeft, ControllerInput.DPadRight,
        ControllerInput.A, ControllerInput.B, ControllerInput.Back
    };

    public event EventHandler? ToggleRequested;

    /// <summary>
    /// Sets the overlay window reference for controller navigation.
    /// When set to non-null, navigation inputs are routed to the window.
    /// When set to null, navigation is disabled.
    /// </summary>
    public void SetOverlayWindow(OverlayWindow? window)
    {
        overlayWindow = window;
        if (window != null)
        {
            // Copy all currently-pressed navigation buttons to consumed to prevent ghost inputs
            foreach (var kvp in pressedButtons)
            {
                var consumed = GetOrCreateButtonSet(consumedButtons, kvp.Key);
                foreach (var button in kvp.Value)
                {
                    if (NavigationButtons.Contains(button))
                    {
                        consumed.Add(button);
                    }
                }
            }
        }
        else
        {
            consumedButtons.Clear();
        }
    }

    /// <summary>
    /// Starts hotkey input listening.
    /// </summary>
    public void Start()
    {
        StartHotkey();
    }

    /// <summary>
    /// Stops hotkey input listening.
    /// </summary>
    public void Stop()
    {
        StopHotkey();
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
    /// Enables controller input processing at runtime.
    /// Used for dynamic enable/disable based on game context (e.g., PC Games Only filter).
    /// </summary>
    public void EnableControllerInput()
    {
        runtimeControllerEnabled = true;
    }

    /// <summary>
    /// Disables controller input processing at runtime.
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

    /// <summary>
    /// Handle a controller button event from Playnite SDK.
    /// Called by OverlayPlugin when it receives SDK controller input events.
    /// </summary>
    public void HandleControllerButtonEvent(ControllerInput button, ControllerInputState state, int controllerInstanceId)
    {
        if (!enableController || !runtimeControllerEnabled || button == ControllerInput.None)
        {
            return;
        }

        if (state == ControllerInputState.Pressed)
        {
            HandleButtonPressed(button, controllerInstanceId);
        }
        else if (state == ControllerInputState.Released)
        {
            HandleButtonReleased(button, controllerInstanceId);
        }
    }

    /// <summary>
    /// Clean up state for a disconnected controller.
    /// </summary>
    public void HandleControllerDisconnected(int controllerInstanceId)
    {
        pressedButtons.Remove(controllerInstanceId);
        consumedButtons.Remove(controllerInstanceId);
        comboTriggered.Remove(controllerInstanceId);
    }

    private void HandleButtonPressed(ControllerInput button, int controllerInstanceId)
    {
        var pressed = GetOrCreateButtonSet(pressedButtons, controllerInstanceId);
        pressed.Add(button);

        // Check toggle combo
        var comboMask = ResolveComboMask(controllerCombo);
        if (comboMask.Length > 0)
        {
            bool allComboPressed = true;
            foreach (var comboButton in comboMask)
            {
                if (!pressed.Contains(comboButton))
                {
                    allComboPressed = false;
                    break;
                }
            }

            comboTriggered.TryGetValue(controllerInstanceId, out var wasTriggered);
            if (allComboPressed && !wasTriggered)
            {
                var elapsed = (DateTime.Now - lastToggleTime).TotalMilliseconds;
                if (elapsed >= ToggleCooldownMs)
                {
                    lastToggleTime = DateTime.Now;
                    comboTriggered[controllerInstanceId] = true;
                    TriggerToggle();
                }
            }
        }

        // Handle navigation if overlay is open
        var currentOverlay = overlayWindow;
        if (currentOverlay != null && NavigationButtons.Contains(button))
        {
            var consumed = GetOrCreateButtonSet(consumedButtons, controllerInstanceId);
            if (!consumed.Contains(button))
            {
                consumed.Add(button);
                DispatchNavigation(currentOverlay, button);
            }
        }
    }

    private void HandleButtonReleased(ControllerInput button, int controllerInstanceId)
    {
        if (pressedButtons.TryGetValue(controllerInstanceId, out var pressed))
        {
            pressed.Remove(button);
        }

        if (consumedButtons.TryGetValue(controllerInstanceId, out var consumed))
        {
            consumed.Remove(button);
        }

        // If any combo button was released, reset the triggered flag so the combo can fire again
        var comboMask = ResolveComboMask(controllerCombo);
        if (comboMask.Length > 0)
        {
            bool isComboButton = false;
            foreach (var comboButton in comboMask)
            {
                if (comboButton == button)
                {
                    isComboButton = true;
                    break;
                }
            }

            if (isComboButton)
            {
                comboTriggered[controllerInstanceId] = false;
            }
        }
    }

    private void DispatchNavigation(OverlayWindow window, ControllerInput button)
    {
        switch (button)
        {
            case ControllerInput.DPadUp:
                Dispatch(window, () => window.ControllerNavigateUp());
                break;
            case ControllerInput.DPadDown:
                Dispatch(window, () => window.ControllerNavigateDown());
                break;
            case ControllerInput.DPadLeft:
                Dispatch(window, () => window.ControllerNavigateLeft());
                break;
            case ControllerInput.DPadRight:
                Dispatch(window, () => window.ControllerNavigateRight());
                break;
            case ControllerInput.A:
                Dispatch(window, () => window.ControllerAccept());
                break;
            case ControllerInput.B:
            case ControllerInput.Back:
                Dispatch(window, () => window.ControllerCancel());
                break;
        }
    }

    private static HashSet<T> GetOrCreateButtonSet<T>(Dictionary<int, HashSet<T>> dict, int controllerInstanceId)
    {
        if (!dict.TryGetValue(controllerInstanceId, out var set))
        {
            set = new HashSet<T>();
            dict[controllerInstanceId] = set;
        }
        return set;
    }

    private static ControllerInput[] ResolveComboMask(string combo)
    {
        var upper = combo.ToUpperInvariant();
        return upper switch
        {
            "GUIDE" => new[] { ControllerInput.Guide },
            "START+BACK" or "BACK+START" => new[] { ControllerInput.Start, ControllerInput.Back },
            "LB+RB" or "RB+LB" => new[] { ControllerInput.LeftShoulder, ControllerInput.RightShoulder },
            _ => Array.Empty<ControllerInput>()
        };
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
                return;
            }
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
                    hotkeyRetryTimer?.Stop();
                    hotkeyRetryTimer = null;
                }
                else if (attempts >= HotkeyRetryLimit)
                {
                    hotkeyRetryTimer?.Stop();
                    hotkeyRetryTimer = null;
                }
            };
            hotkeyRetryTimer.Start();
        });
    }
}
