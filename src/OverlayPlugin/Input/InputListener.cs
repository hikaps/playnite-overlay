using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using PlayniteOverlay;

namespace PlayniteOverlay.Input;

internal sealed class InputListener
{
    private const int PollIntervalMs = 50;
    private const int HotkeyRetryLimit = 10;

    private readonly ushort[] lastButtons = new ushort[4];

    private Timer? pollTimer;
    private HotkeyManager? hotkeyManager;
    private DispatcherTimer? hotkeyRetryTimer;
    private string controllerCombo = "Guide";
    private string? customHotkeyGesture;
    private bool enableController = true;

    public event EventHandler? ToggleRequested;

    public void Start()
    {
        TryRegisterHotkey();
        pollTimer ??= new Timer(_ => PollControllers(), null, 0, PollIntervalMs);
    }

    public void Stop()
    {
        pollTimer?.Dispose();
        pollTimer = null;

        Application.Current?.Dispatcher.Invoke(() =>
        {
            hotkeyRetryTimer?.Stop();
            hotkeyRetryTimer = null;
            hotkeyManager?.Unregister();
        });
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
            return;
        }

        for (int index = 0; index < lastButtons.Length; index++)
        {
            if (controllerCombo.Equals("Guide", StringComparison.OrdinalIgnoreCase) && TryHandleGuideButton(index))
            {
                continue;
            }

            if (!XInput.TryGetState(index, out var state))
            {
                continue;
            }

            var buttons = state.Gamepad.wButtons;
            var mask = ResolveComboMask(controllerCombo);
            if (mask != 0)
            {
                bool now = (buttons & mask) == mask;
                bool prev = (lastButtons[index] & mask) == mask;
                if (now && !prev)
                {
                    TriggerToggle();
                }
            }

            lastButtons[index] = buttons;
        }
    }

    private bool TryHandleGuideButton(int index)
    {
        if (XInput.TryGetKeystroke(index, out var stroke)
            && (stroke.Flags & XInput.XINPUT_KEYSTROKE_KEYDOWN) != 0
            && stroke.VirtualKey == XInput.VK_PAD_GUIDE_BUTTON)
        {
            TriggerToggle();
            return true;
        }

        return false;
    }

    private static ushort ResolveComboMask(string combo)
    {
        var upper = combo.ToUpperInvariant();
        return upper switch
        {
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
                if (hotkeyManager.Register(customHotkeyGesture!, TriggerToggle) || attempts >= HotkeyRetryLimit)
                {
                    hotkeyRetryTimer?.Stop();
                    hotkeyRetryTimer = null;
                }
            };
            hotkeyRetryTimer.Start();
        });
    }
}
