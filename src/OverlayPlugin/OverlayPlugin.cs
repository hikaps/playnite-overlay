using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
    public static readonly Guid PluginId = new("11111111-2222-3333-4444-555555555555");

    private readonly ILogger logger;
    private readonly InputListener input;
    private readonly OverlayService overlay;
    private readonly GameSwitcher switcher;
    private readonly RunningAppsDetector runningAppsDetector;
    private readonly SuccessStoryIntegration successStory;
    private readonly AudioDeviceService? audioDeviceService;
    private readonly GameVolumeService? gameVolumeService;
    private readonly OverlaySettingsViewModel settings;
    private readonly Dictionary<Guid, BorderlessHelper.WindowState> borderlessStates = new Dictionary<Guid, BorderlessHelper.WindowState>();
    private bool isDisposed;

    public OverlayPlugin(IPlayniteAPI api) : base(api)
    {
        logger = LogManager.GetLogger();
        input = new InputListener();
        overlay = new OverlayService(input);
        settings = new OverlaySettingsViewModel(this);
        switcher = new GameSwitcher(api, settings.Settings);
        runningAppsDetector = new RunningAppsDetector(api, settings.Settings);
        successStory = new SuccessStoryIntegration(api);

        // Initialize audio device service (optional - may fail if NAudio unavailable)
        try
        {
            audioDeviceService = new AudioDeviceService();
        }
        catch (Exception ex)
        {
            logger.Warn(ex, "Failed to initialize AudioDeviceService - audio switching will be disabled");
            audioDeviceService = null;
        }

        // Initialize game volume service (optional - may fail if NAudio unavailable)
        try
        {
            gameVolumeService = new GameVolumeService();
        }
        catch (Exception ex)
        {
            logger.Warn(ex, "Failed to initialize GameVolumeService - volume control will be disabled");
            gameVolumeService = null;
        }

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

        // Check if we should enable controller for this game
        bool shouldEnableController = !settings.Settings.PcGamesOnly || IsPcGame(args.Game);

        if (shouldEnableController)
        {
            // Enable controller input for this game
            input.EnableControllerInput();
            
            // Start controller timer if not already running (when not always-active)
            if (!settings.Settings.ControllerAlwaysActive)
            {
                input.StartController();
            }
        }
        else
        {
            // Disable controller input for non-PC games when PcGamesOnly is enabled
            logger.Info($"Disabling controller input for non-PC game: {args.Game.Name}");
            input.DisableControllerInput();
        }

        // Apply borderless mode if enabled and we have a process ID
        if (settings.Settings.ForceBorderlessMode && args.StartedProcessId > 0)
        {
            ApplyBorderlessAsync(args.Game.Id, args.StartedProcessId, args.Game.Name);
        }
    }

    public override void OnGameStopped(OnGameStoppedEventArgs args)
    {
        // Restore borderless window state if we modified it
        RestoreBorderlessWindow(args.Game.Id, args.Game.Name);

        // Clear active app when game stops
        switcher.ClearActiveApp();
        
        // Re-enable controller input after game stops (in case it was disabled for non-PC game)
        input.EnableControllerInput();
        
        // Stop controller input only if not configured to be always-active
        if (!settings.Settings.ControllerAlwaysActive)
        {
            input.StopController();
        }
        
        overlay.Hide();
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

            // Populate achievements if enabled and game has a valid ID
            if (settings.Settings.ShowAchievements && currentGameItem.GameId != Guid.Empty)
            {
                currentGameItem.Achievements = successStory.GetGameAchievements(
                    currentGameItem.GameId,
                    settings.Settings.MaxRecentAchievements,
                    settings.Settings.MaxLockedAchievements);
            }
        }
        else if (switcher.ActiveApp != null)
        {
            // Active app closed - clear it
            logger.Info($"Active app is no longer valid, clearing: {switcher.ActiveApp.Title}");
            switcher.ClearActiveApp();
        }

        // Get running apps (excluding active app)
        // Set the active app's window handle so SwitchToApp knows what to minimize
        runningAppsDetector.ActiveAppWindowHandle = switcher.ActiveApp?.WindowHandle ?? IntPtr.Zero;
        var runningApps = runningAppsDetector.GetRunningApps(
            excludeFromRunningApps,
            settings.Settings.ShowGenericApps,
            settings.Settings.MaxRunningApps);

        // Extract game IDs of running Playnite games to exclude from recent list
        var runningGameIds = new HashSet<Guid>(
            runningApps
                .Where(a => a.Type == AppType.PlayniteGame && a.GameId.HasValue)
                .Select(a => a.GameId!.Value));

        // Build recent games list (excludes active app and running apps)
        var recentGames = switcher.GetRecentGames(5, runningGameIds)
            .Select(g => OverlayItem.FromRecentGame(g, switcher))
            .ToList();

        // Get audio devices (null if service unavailable or enumeration fails)
        IEnumerable<AudioDevice>? audioDevices = null;
        try
        {
            audioDevices = audioDeviceService?.GetOutputDevices();
        }
        catch (Exception ex)
        {
            logger.Debug(ex, "Error getting audio devices");
        }

        overlay.Show(
            () => switcher.SwitchToPlaynite(),
            HandleExitGame,
            currentGameItem,
            runningApps,
            recentGames,
            audioDevices,
            SwitchAudioDevice,
            gameVolumeService,
            switcher.ActiveApp?.ProcessId,
            switcher,
            settings.Settings,
            settings.Settings.Shortcuts,
            settings.Settings.ShouldSuspendGame,
            settings.Settings.ShouldMinimizeGame,
            switcher.ActiveApp?.WindowHandle ?? IntPtr.Zero);
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

    private void SwitchAudioDevice(string deviceId, Action<bool> onComplete)
    {
        if (audioDeviceService == null)
        {
            logger.Warn("Cannot switch audio device: AudioDeviceService not initialized");
            onComplete(false);
            return;
        }

        try
        {
            var success = audioDeviceService.SetDefaultDevice(deviceId);
            if (success)
            {
                logger.Info($"Successfully switched audio device to {deviceId}");
                onComplete(true);
            }
            else
            {
                logger.Warn($"Failed to switch audio device to {deviceId}");
                onComplete(false);
            }
        }
        catch (Exception ex)
        {
            logger.Error(ex, $"Error switching audio device to {deviceId}");
            onComplete(false);
        }
    }

    private async void ApplyBorderlessAsync(Guid gameId, int processId, string gameName)
    {
        try
        {
            await Task.Delay(settings.Settings.BorderlessDelayMs);

            // Check if game is still running
            var hwnd = BorderlessHelper.GetMainWindowHandle(processId);
            if (hwnd == IntPtr.Zero)
            {
                logger.Debug($"Game window not found for {gameName}, skipping borderless mode");
                return;
            }

            // Skip if window is already borderless or in exclusive fullscreen
            if (!BorderlessHelper.HasWindowBorders(hwnd))
            {
                logger.Debug($"{gameName} already borderless or fullscreen, skipping");
                return;
            }

            if (BorderlessHelper.IsLikelyExclusiveFullscreen(hwnd))
            {
                logger.Debug($"{gameName} appears to be in exclusive fullscreen, skipping");
                return;
            }

            var state = BorderlessHelper.MakeBorderless(hwnd);
            if (state != null)
            {
                borderlessStates[gameId] = state;
                logger.Info($"Applied borderless mode to {gameName}");
            }
            else
            {
                logger.Debug($"Failed to apply borderless mode to {gameName}");
            }
        }
        catch (Exception ex)
        {
            logger.Error(ex, $"Error applying borderless mode to {gameName}");
        }
    }

    private void RestoreBorderlessWindow(Guid gameId, string gameName)
    {
        if (borderlessStates.TryGetValue(gameId, out var state))
        {
            if (BorderlessHelper.RestoreWindow(state))
            {
                logger.Info($"Restored window state for {gameName}");
            }
            borderlessStates.Remove(gameId);
        }
    }

    /// <summary>
    /// Checks if a game is a PC game based on its platform metadata.
    /// Returns true if the game has a PC platform, or if no platform info is available (backward compatible).
    /// </summary>
    private static bool IsPcGame(Playnite.SDK.Models.Game game)
    {
        if (game.Platforms == null || !game.Platforms.Any())
        {
            // No platform info - assume PC (backward compatible)
            return true;
        }

        return game.Platforms.Any(p =>
            p.Name != null && (
                p.Name.Equals("PC", StringComparison.OrdinalIgnoreCase) ||
                p.Name.Equals("PC (Windows)", StringComparison.OrdinalIgnoreCase) ||
                p.Name.IndexOf("Windows", StringComparison.OrdinalIgnoreCase) >= 0
            ));
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
        audioDeviceService?.Dispose();
        gameVolumeService?.Dispose();

        base.Dispose();
    }
}
