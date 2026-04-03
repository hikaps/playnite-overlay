using System;
using System.Collections.Generic;
using MVVM = CommunityToolkit.Mvvm.ComponentModel;
using Playnite.SDK;

namespace PlayniteOverlay.Services;

internal sealed class ControllerDiagnosticService : MVVM.ObservableObject, IDisposable
{
    private static readonly ILogger logger = LogManager.GetLogger();

    private System.Threading.Timer? pollTimer;
    private bool isPolling;

    public bool IsPolling
    {
        get => isPolling;
        private set => SetProperty(ref isPolling, value);
    }

    public string StatusText
    {
        get => statusText;
        private set => SetProperty(ref statusText, value);
    }

    public List<DiagnosticDevice> Devices
    {
        get => devices;
        private set => SetProperty(ref devices, value);
    }

    private string statusText = string.Empty;
    private List<DiagnosticDevice> devices = new List<DiagnosticDevice>();

    public void StartPolling()
    {
        if (isPolling) return;

        if (!SDL2.Init())
        {
            StatusText = "SDL2 failed to initialize. Check Playnite logs for details.";
            return;
        }

        isPolling = true;
        pollTimer = new System.Threading.Timer(_ => Poll(), null, 0, 200);
        logger.Info("Controller diagnostic polling started.");
    }

    public void StopPolling()
    {
        if (!isPolling) return;

        pollTimer?.Dispose();
        pollTimer = null;
        isPolling = false;
        StatusText = string.Empty;
        Devices = new List<DiagnosticDevice>();
        logger.Info("Controller diagnostic polling stopped.");
    }

    private void Poll()
    {
        try
        {
            SDL2.GameControllerUpdate();
            SDL2.JoystickUpdate();

            var newDevices = new List<DiagnosticDevice>();
            var numJoysticks = SDL2.NumJoysticks();

            for (int i = 0; i < numJoysticks; i++)
            {
                var name = SDL2.JoystickNameForIndex(i) ?? $"Unknown device {i}";
                var isGameController = SDL2.IsGameController(i);
                var hasMapping = SDL2.GameControllerMappingForDeviceIndex(i) != null;

                IntPtr handle;
                bool usingGameController;
                if (isGameController)
                {
                    handle = SDL2.GameControllerOpen(i);
                    usingGameController = true;
                }
                else
                {
                    handle = SDL2.JoystickOpen(i);
                    usingGameController = false;
                }

                var buttons = new List<DiagnosticButton>();

                if (handle != IntPtr.Zero)
                {
                    if (usingGameController)
                    {
                        foreach (var btn in GameControllerButtonNames)
                        {
                            var pressed = SDL2.GameControllerGetButton(handle, btn.Key) == 1;
                            buttons.Add(new DiagnosticButton(btn.Value, pressed));
                        }
                    }
                    else
                    {
                        var numButtons = SDL2.JoystickNumButtons(handle);
                        for (int b = 0; b < Math.Min(numButtons, 20); b++)
                        {
                            var pressed = SDL2.JoystickGetButton(handle, b) == 1;
                            buttons.Add(new DiagnosticButton($"Button {b}", pressed));
                        }
                    }

                    if (usingGameController)
                    {
                        SDL2.GameControllerClose(handle);
                    }
                    else
                    {
                        SDL2.JoystickClose(handle);
                    }
                }

                newDevices.Add(new DiagnosticDevice(name, isGameController, hasMapping, buttons));
            }

            Devices = newDevices;
            StatusText = numJoysticks == 0
                ? "No joysticks/controllers detected. Connect a controller and press buttons."
                : $"{numJoysticks} device(s) found. Press buttons to verify input.";
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Controller diagnostic poll error.");
        }
    }

    private static readonly Dictionary<int, string> GameControllerButtonNames = new Dictionary<int, string>
    {
        { SDL2.SDL_CONTROLLER_BUTTON_A, "A" },
        { SDL2.SDL_CONTROLLER_BUTTON_B, "B" },
        { SDL2.SDL_CONTROLLER_BUTTON_X, "X" },
        { SDL2.SDL_CONTROLLER_BUTTON_Y, "Y" },
        { SDL2.SDL_CONTROLLER_BUTTON_BACK, "Back" },
        { SDL2.SDL_CONTROLLER_BUTTON_GUIDE, "Guide" },
        { SDL2.SDL_CONTROLLER_BUTTON_START, "Start" },
        { SDL2.SDL_CONTROLLER_BUTTON_LEFTSTICK, "L.Stick" },
        { SDL2.SDL_CONTROLLER_BUTTON_RIGHTSTICK, "R.Stick" },
        { SDL2.SDL_CONTROLLER_BUTTON_LEFTSHOULDER, "LB" },
        { SDL2.SDL_CONTROLLER_BUTTON_RIGHTSHOULDER, "RB" },
        { SDL2.SDL_CONTROLLER_BUTTON_DPAD_UP, "D.Up" },
        { SDL2.SDL_CONTROLLER_BUTTON_DPAD_DOWN, "D.Down" },
        { SDL2.SDL_CONTROLLER_BUTTON_DPAD_LEFT, "D.Left" },
        { SDL2.SDL_CONTROLLER_BUTTON_DPAD_RIGHT, "D.Right" },
    };

    public void Dispose()
    {
        StopPolling();
    }
}

internal sealed class DiagnosticDevice
{
    public string Name { get; }
    public bool IsGameController { get; }
    public bool HasMapping { get; }
    public List<DiagnosticButton> Buttons { get; }

    public DiagnosticDevice(string name, bool isGameController, bool hasMapping, List<DiagnosticButton> buttons)
    {
        Name = name;
        IsGameController = isGameController;
        HasMapping = hasMapping;
        Buttons = buttons;
    }
}

internal sealed class DiagnosticButton
{
    public string Name { get; }
    public bool IsPressed { get; }

    public DiagnosticButton(string name, bool isPressed)
    {
        Name = name;
        IsPressed = isPressed;
    }
}
