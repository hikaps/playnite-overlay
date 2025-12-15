using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Plugins;
using PlayniteOverlay.Input;
using PlayniteOverlay.Models;
using PlayniteOverlay.Services;

namespace PlayniteOverlay;

public class OverlayPlugin : GenericPlugin
{
    private const string MenuSection = "&Overlay";
    private const string ToggleOverlayDescription = "Toggle Overlay";
    public static readonly Guid PluginId = new("11111111-2222-3333-4444-555555555555");

    private readonly ILogger logger;
    private readonly InputListener input;
    private readonly OverlayService overlay;
    private readonly GameSwitcher switcher;
    private readonly RunningAppsDetector runningAppsDetector;
    private readonly OverlaySettingsViewModel settings;
    private bool isDisposed;

    public OverlayPlugin(IPlayniteAPI api) : base(api)
    {
        logger = LogManager.GetLogger();
        input = new InputListener();
        overlay = new OverlayService();
        switcher = new GameSwitcher(api);
        runningAppsDetector = new RunningAppsDetector(api);
        settings = new OverlaySettingsViewModel(this);

        Properties = new GenericPluginProperties
        {
            HasSettings = true
        };

        try
        {
            var existing = LoadPluginSettings<OverlaySettings>();
            if (existing == null)
            {
                SavePluginSettings(settings.Settings);
            }
        }
        catch (Exception ex)
        {
            logger.Debug(ex, "Failed to load plugin settings, using defaults.");
        }

        input.ApplySettings(settings.Settings);
        input.ToggleRequested += HandleToggleRequested;

        // Subscribe to app switch events to update active app tracking
        runningAppsDetector.AppSwitched += (sender, app) =>
        {
            switcher.SetActiveApp(app);
        };

        // Start hotkey immediately (keyboard shortcut should always work)
        input.StartHotkey();

        // Start controller input if configured to be always active
        if (settings.Settings.ControllerAlwaysActive)
        {
            input.StartController();
        }
    }

    public override Guid Id => PluginId;

    public override void OnGameStarted(OnGameStartedEventArgs args)
    {
        // Set the game as active app
        switcher.SetActiveFromGame(args.Game);
        
        // Start controller input if not already running (when not always-active)
        if (!settings.Settings.ControllerAlwaysActive)
        {
            input.StartController();
        }
    }

    public override void OnGameStopped(OnGameStoppedEventArgs args)
    {
        // Clear active app when game stops
        switcher.ClearActiveApp();
        
        // Stop controller input only if not configured to be always-active
        if (!settings.Settings.ControllerAlwaysActive)
        {
            input.StopController();
        }
        
        overlay.Hide();
    }

    public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
    {
        yield return new MainMenuItem
        {
            Description = ToggleOverlayDescription,
            MenuSection = MenuSection,
            Action = _ => ToggleOverlay()
        };
    }

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
        
        // Apply controller always-active setting
        if (newSettings.ControllerAlwaysActive)
        {
            input.StartController();
        }
        else if (switcher.ActiveApp == null)
        {
            // Stop controller if no active app and not always-active
            input.StopController();
        }
    }

    private void HandleToggleRequested(object? sender, EventArgs e)
    {
        ToggleOverlay();
    }

    private void ToggleOverlay()
    {
        logger.Info($"Toggling overlay (visible={overlay.IsVisible})");
        if (overlay.IsVisible)
        {
            overlay.Hide();
            return;
        }

        // Build current game item from active app (single source of truth)
        OverlayItem? currentGameItem = null;
        Guid? excludeFromRunningApps = null;

        // Auto-detect foreground app if no active app
        if (switcher.ActiveApp == null)
        {
            var foregroundApp = switcher.DetectForegroundApp(
                settings.Settings.ShowGenericApps,
                settings.Settings.MaxRunningApps);
            
            if (foregroundApp != null)
            {
                switcher.SetActiveApp(foregroundApp);
                logger.Info($"Auto-detected foreground app on overlay open: {foregroundApp.Title}");
            }
        }

        if (switcher.ActiveApp != null && switcher.IsActiveAppStillValid())
        {
            // Active app is valid - use it for NOW PLAYING
            currentGameItem = OverlayItem.FromRunningApp(switcher.ActiveApp, switcher);
            excludeFromRunningApps = switcher.ActiveApp.GameId;
            logger.Debug($"Using active app for NOW PLAYING: {switcher.ActiveApp.Title}");
        }
        else if (switcher.ActiveApp != null)
        {
            // Active app closed - clear it
            logger.Info($"Active app is no longer valid, clearing: {switcher.ActiveApp.Title}");
            switcher.ClearActiveApp();
        }

        // Get running apps (excluding active app)
        var runningApps = runningAppsDetector.GetRunningApps(
            excludeFromRunningApps,
            settings.Settings.ShowGenericApps,
            settings.Settings.MaxRunningApps);

        // Build recent games list (excludes active app if it's a game)
        var recentGames = switcher.GetRecentGames(5)
            .Select(g => OverlayItem.FromRecentGame(g, switcher))
            .ToList();

        overlay.Show(
            () => switcher.SwitchToPlaynite(),
            HandleExitGame,
            currentGameItem,
            runningApps,
            recentGames,
            settings.Settings.UseControllerToOpen);
    }

    private void HandleExitGame()
    {
        // Exit the active app (whatever is in NOW PLAYING)
        switcher.ExitActiveApp();
        switcher.ClearActiveApp();
        
        // Auto-detect new foreground app after exit
        var foregroundApp = switcher.DetectForegroundApp(
            settings.Settings.ShowGenericApps,
            settings.Settings.MaxRunningApps);
        
        if (foregroundApp != null)
        {
            switcher.SetActiveApp(foregroundApp);
            logger.Info($"Auto-detected foreground app after exit: {foregroundApp.Title}");
        }
    }

    public override void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;

        // Unsubscribe from events to prevent memory leaks
        input.ToggleRequested -= HandleToggleRequested;

        // Clean up resources
        input.Stop();
        overlay.Hide();

        base.Dispose();
    }
}
