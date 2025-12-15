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
    private readonly OverlaySettingsViewModel settings;
    private bool isDisposed;

    public OverlayPlugin(IPlayniteAPI api) : base(api)
    {
        logger = LogManager.GetLogger();
        input = new InputListener();
        overlay = new OverlayService();
        switcher = new GameSwitcher(api);
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

        // Build current game item (if any)
        OverlayItem? currentGameItem = null;
        if (switcher.CurrentGame != null)
        {
            currentGameItem = OverlayItem.FromCurrentGame(switcher.CurrentGame, switcher);
        }

        // Build recent games list (excludes current game)
        var recentGames = switcher.GetRecentGames(5)
            .Select(g => OverlayItem.FromRecentGame(g, switcher))
            .ToList();

        overlay.Show(
            () => switcher.SwitchToPlaynite(),
            () => switcher.ExitCurrent(),
            currentGameItem,
            recentGames,
            settings.Settings.UseControllerToOpen);
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
