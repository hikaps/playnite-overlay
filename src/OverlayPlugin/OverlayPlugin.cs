using System;
using System.Threading;
using System.Collections.Generic;
using System.Windows.Controls;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Plugins;

namespace PlayniteOverlay;

public class OverlayPlugin : GenericPlugin
{
    private const string MenuSection = "&Overlay";
    private const string ToggleOverlayDescription = "Toggle Overlay";
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

        // Enable settings page in Playnite
        Properties = new GenericPluginProperties
        {
            HasSettings = true
        };

        input.ApplySettings(settings.Settings);

        input.ToggleRequested += (_, __) => ToggleOverlay();
    }

    public override Guid Id => PluginId;

    

    public override void OnGameStarted(OnGameStartedEventArgs args)
    {
        switcher.SetCurrent(args.Game);
        input.Start();
    }

    public override void OnGameStopped(OnGameStoppedEventArgs args)
    {
        switcher.ClearCurrent();
        input.Stop();
        overlay.Hide();
    }

    private void ToggleOverlay()
    {
        logger.Info($"Toggling overlay (visible={overlay.IsVisible})");
        if (overlay.IsVisible)
        {
            overlay.Hide();
        }
        else
        {
            var title = switcher.CurrentGameTitle ?? "Overlay";
            overlay.Show(() =>
            {
                // Example callbacks; wire to UI commands in OverlayUI
                switcher.SwitchToNextRecommended();
            },
            () =>
            {
                switcher.ExitCurrent();
            }, title);
        }
    }

    // Expose menu action so users can bind Playnite shortcuts
    public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
    {
        yield return new MainMenuItem
        {
            Description = ToggleOverlayDescription,
            MenuSection = MenuSection,
            Action = _ => ToggleOverlay()
        };
    }

    // Settings plumbing
    public override ISettings GetSettings(bool firstRunSettings) => settings;
    public override UserControl GetSettingsView(bool firstRunSettings)
    {
        try
        {
            return new OverlaySettingsView { DataContext = settings };
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to create settings view, falling back to basic UI.");
            var stack = new StackPanel { Margin = new System.Windows.Thickness(10) };
            stack.Children.Add(new TextBlock { Text = "Playnite Overlay Settings", FontWeight = System.Windows.FontWeights.Bold, FontSize = 14 });
            stack.Children.Add(new TextBlock { Text = "Settings UI failed to load. You can still edit settings in JSON or retry after restart.", TextWrapping = System.Windows.TextWrapping.Wrap });
            return new UserControl { Content = stack };
        }
    }

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
                    ushort mask = 0;
                    var combo = controllerCombo.ToUpperInvariant();
                    if (combo == "START+BACK" || combo == "BACK+START")
                    {
                        mask = (ushort)(XInput.XINPUT_GAMEPAD_START | XInput.XINPUT_GAMEPAD_BACK);
                    }
                    else if (combo == "LB+RB" || combo == "RB+LB")
                    {
                        mask = (ushort)(XInput.XINPUT_GAMEPAD_LEFT_SHOULDER | XInput.XINPUT_GAMEPAD_RIGHT_SHOULDER);
                    }

                    if (mask != 0)
                    {
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
    private OverlayWindow? window;

    public void Show(Action onSwitch, Action onExit, string title)
    {
        if (IsVisible)
        {
            return;
        }
        IsVisible = true;
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            window = new OverlayWindow(onSwitch, onExit, title)
            {
                Topmost = true
            };
            window.Loaded += (_, __) =>
            {
                var px = Monitors.GetActiveMonitorBoundsInPixels();
                var dips = Monitors.PixelsToDips(window, px);
                window.Left = dips.Left;
                window.Top = dips.Top;
                window.Width = dips.Width;
                window.Height = dips.Height;
            };
            window.Closed += (_, __) => { IsVisible = false; window = null; };
            window.Show();
        });
    }

    public void Hide()
    {
        if (!IsVisible)
        {
            return;
        }
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            try { window?.Close(); } catch { /* ignore */ }
            window = null;
        });
        IsVisible = false;
    }
}

public class GameSwitcher
{
    private readonly IPlayniteAPI api;
    private Playnite.SDK.Models.Game? current;
    public GameSwitcher(IPlayniteAPI api) => this.api = api;

    public void SwitchToNextRecommended()
    {
        // TODO: Use api.Database.Games and api.StartGame(game) to switch.
    }

    public void ExitCurrent()
    {
        // TODO: Ask Playnite to stop current game or kill process with confirmation.
    }

    public void SetCurrent(Playnite.SDK.Models.Game? game)
    {
        current = game;
    }

    public void ClearCurrent()
    {
        current = null;
    }

    public string? CurrentGameTitle => current?.Name;
}
