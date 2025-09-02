using System;
using System.Threading;
using Playnite.SDK;
using Playnite.SDK.Plugins;
using System.Collections.Generic;
using System.Windows.Controls;
using Playnite.SDK.Events;

namespace PlayniteOverlay;

public class OverlayPlugin : GenericPlugin
{
    public static readonly Guid PluginId = new ("11111111-2222-3333-4444-555555555555");

    private readonly ILogger logger;
    private readonly InputListener input;
    private readonly OverlayService overlay;
    private readonly GameSwitcher switcher;
    private readonly OverlaySettingsViewModel settings;

    public OverlayPlugin(IPlayniteAPI api) : base(api)
    {
        logger = LogManager.GetLogger();
        input = new InputListener();
        overlay = new OverlayService();
        switcher = new GameSwitcher(api);
        settings = new OverlaySettingsViewModel(this);

        input.ApplySettings(settings.Settings);

        input.ToggleRequested += (_, __) => ToggleOverlay();
    }

    public override Guid Id => PluginId;

    public override void OnGameStarted(OnGameStartedEventArgs args)
    {
        input.Start();
    }

    public override void OnGameStopped(OnGameStoppedEventArgs args)
    {
        input.Stop();
        overlay.Hide();
    }

    private void ToggleOverlay()
    {
        if (overlay.IsVisible)
        {
            overlay.Hide();
        }
        else
        {
            overlay.Show(() =>
            {
                // Example callbacks; wire to UI commands in OverlayUI
                switcher.SwitchToNextRecommended();
            },
            () =>
            {
                switcher.ExitCurrent();
            });
        }
    }

    // Expose menu action so users can bind Playnite shortcuts
    public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
    {
        yield return new MainMenuItem
        {
            Description = "Toggle Overlay",
            MenuSection = "&Overlay",
            Action = _ => ToggleOverlay()
        };
    }

    // Settings plumbing
    public override ISettings GetSettings(bool firstRunSettings) => settings;
    public override UserControl GetSettingsView(bool firstRunSettings) => new OverlaySettingsView { DataContext = settings };

    internal void ApplySettings(OverlaySettings newSettings)
    {
        input.ApplySettings(newSettings);
    }
}

public class InputListener
{
    private Timer? timer;
    public event EventHandler? ToggleRequested;
    private string? customHotkeyGesture;
    private bool enableController = true;
    private HotkeyManager? hotkey;
    private readonly ushort[] lastButtons = new ushort[4];
    private string controllerCombo = "Guide";

    public void Start()
    {
        // Register global hotkey on UI thread if enabled
        if (!string.IsNullOrWhiteSpace(customHotkeyGesture))
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                hotkey ??= new HotkeyManager();
                hotkey.Register(customHotkeyGesture!, () => TriggerToggle());
            });
        }

        timer ??= new Timer(_ =>
        {
            // Controller polling
            if (enableController)
            {
                // Check up to 4 controllers
                for (int i = 0; i < 4; i++)
                {
                    // If Guide requested, try keystroke first (edge-triggered)
                    if (controllerCombo.Equals("Guide", StringComparison.OrdinalIgnoreCase))
                    {
                        if (XInput.TryGetKeystroke(i, out var stroke))
                        {
                            if ((stroke.Flags & XInput.XINPUT_KEYSTROKE_KEYDOWN) != 0 && stroke.VirtualKey == XInput.VK_PAD_GUIDE_BUTTON)
                            {
                                TriggerToggle();
                                continue;
                            }
                        }
                    }

                    // Fallbacks/other combos via state polling
                    if (!XInput.TryGetState(i, out var state))
                        continue;

                    var buttons = state.Gamepad.wButtons;

                    if (controllerCombo.Equals("Start+Back", StringComparison.OrdinalIgnoreCase) || controllerCombo.Equals("Back+Start", StringComparison.OrdinalIgnoreCase))
                    {
                        ushort mask = (ushort)(XInput.XINPUT_GAMEPAD_START | XInput.XINPUT_GAMEPAD_BACK);
                        bool now = (buttons & mask) == mask;
                        bool prev = (lastButtons[i] & mask) == mask;
                        if (now && !prev)
                        {
                            TriggerToggle();
                        }
                    }
                    else if (controllerCombo.Equals("LB+RB", StringComparison.OrdinalIgnoreCase) || controllerCombo.Equals("RB+LB", StringComparison.OrdinalIgnoreCase))
                    {
                        ushort mask = (ushort)(XInput.XINPUT_GAMEPAD_LEFT_SHOULDER | XInput.XINPUT_GAMEPAD_RIGHT_SHOULDER);
                        bool now = (buttons & mask) == mask;
                        bool prev = (lastButtons[i] & mask) == mask;
                        if (now && !prev)
                        {
                            TriggerToggle();
                        }
                    }

                    lastButtons[i] = buttons;
                }
            }
        }, null, 0, 50);
    }

    public void Stop()
    {
        timer?.Dispose();
        timer = null;
        hotkey?.Unregister();
    }

    // For keyboard fallback during dev/testing
    public void TriggerToggle() => ToggleRequested?.Invoke(this, EventArgs.Empty);

    public void ApplySettings(OverlaySettings settings)
    {
        customHotkeyGesture = settings.EnableCustomHotkey ? settings.CustomHotkey : null;
        enableController = settings.UseControllerToOpen;
        controllerCombo = settings.ControllerCombo ?? "Guide";

        // Re-register hotkey on UI thread
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            if (string.IsNullOrWhiteSpace(customHotkeyGesture))
            {
                hotkey?.Unregister();
            }
            else
            {
                hotkey ??= new HotkeyManager();
                hotkey.Register(customHotkeyGesture!, () => TriggerToggle());
            }
        });
    }
}

public class OverlayService
{
    public bool IsVisible { get; private set; }

    public void Show(Action onSwitch, Action onExit)
    {
        IsVisible = true;
        // TODO: Launch WPF overlay window via OverlayUI with provided callbacks.
    }

    public void Hide()
    {
        IsVisible = false;
        // TODO: Close overlay window and restore focus to game.
    }
}

public class GameSwitcher
{
    private readonly IPlayniteAPI api;
    public GameSwitcher(IPlayniteAPI api) => this.api = api;

    public void SwitchToNextRecommended()
    {
        // TODO: Use api.Database.Games and api.StartGame(game) to switch.
    }

    public void ExitCurrent()
    {
        // TODO: Ask Playnite to stop current game or kill process with confirmation.
    }
}
