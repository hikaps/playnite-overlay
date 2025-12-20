using System;
using System.Threading;
using System.Windows.Threading;

namespace PlayniteOverlay.Input;

internal sealed class OverlayControllerNavigator : IDisposable
{
    private const int PollIntervalMs = 100;
    private const int InputCooldownMs = 150;

    private readonly OverlayWindow window;
    private readonly Timer pollTimer;
    private readonly ushort[] lastButtons = new ushort[4];
    private DateTime lastInputTime = DateTime.MinValue;
    private bool isDisposed;

    public OverlayControllerNavigator(OverlayWindow window)
    {
        this.window = window ?? throw new ArgumentNullException(nameof(window));
        pollTimer = new Timer(_ => Poll(), null, 0, PollIntervalMs);
    }

    private void Poll()
    {
        if (isDisposed)
        {
            return;
        }

        // Check window state on UI thread to avoid cross-thread access violations
        bool shouldPoll = false;
        try
        {
            var dispatcher = window.Dispatcher;
            if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
            {
                return;
            }

            if (dispatcher.CheckAccess())
            {
                // Already on UI thread
                shouldPoll = window.IsLoaded && window.IsVisible;
            }
            else
            {
                // Marshal to UI thread
                shouldPoll = (bool)dispatcher.Invoke(
                    () => window.IsLoaded && window.IsVisible,
                    DispatcherPriority.Normal);
            }
        }
        catch (Exception)
        {
            // Dispatcher may be shutting down or window disposed
            return;
        }

        if (!shouldPoll)
        {
            return;
        }

        for (int index = 0; index < lastButtons.Length; index++)
        {
            if (!XInput.TryGetState(index, out var state))
            {
                lastButtons[index] = 0;
                continue;
            }

            var buttons = state.Gamepad.wButtons;

            HandleDirection(index, buttons);
            HandleAction(index, buttons);

            lastButtons[index] = buttons;
        }
    }

    private void HandleDirection(int playerIndex, ushort buttons)
    {
        var previous = lastButtons[playerIndex];
        
        if (IsPressed(previous, buttons, XInput.XINPUT_GAMEPAD_DPAD_UP))
        {
            DispatchWithCooldown(() => window.ControllerNavigateUp());
        }

        if (IsPressed(previous, buttons, XInput.XINPUT_GAMEPAD_DPAD_DOWN))
        {
            DispatchWithCooldown(() => window.ControllerNavigateDown());
        }

        if (IsPressed(previous, buttons, XInput.XINPUT_GAMEPAD_DPAD_LEFT))
        {
            DispatchWithCooldown(() => window.ControllerNavigateLeft());
        }

        if (IsPressed(previous, buttons, XInput.XINPUT_GAMEPAD_DPAD_RIGHT))
        {
            DispatchWithCooldown(() => window.ControllerNavigateRight());
        }
    }

    private void HandleAction(int playerIndex, ushort buttons)
    {
        var previous = lastButtons[playerIndex];

        if (IsPressed(previous, buttons, XInput.XINPUT_GAMEPAD_A))
        {
            DispatchWithCooldown(() => window.ControllerAccept());
        }

        if (IsPressed(previous, buttons, XInput.XINPUT_GAMEPAD_B)
            || IsPressed(previous, buttons, XInput.XINPUT_GAMEPAD_BACK))
        {
            DispatchWithCooldown(() => window.ControllerCancel());
        }
    }

    private void DispatchWithCooldown(Action action)
    {
        if ((DateTime.Now - lastInputTime).TotalMilliseconds < InputCooldownMs)
        {
            return;
        }
        lastInputTime = DateTime.Now;
        Dispatch(action);
    }

    private void Dispatch(Action action)
    {
        if (isDisposed)
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

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        pollTimer.Dispose();
    }
}
