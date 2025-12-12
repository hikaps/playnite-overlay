using System;
using System.Collections.Generic;
using System.Linq;
using Playnite.SDK;
using PlayniteOverlay;

namespace PlayniteOverlay.Services;

public sealed class GameSwitcher
{
    private static readonly ILogger logger = LogManager.GetLogger();
    private readonly IPlayniteAPI api;
    private Playnite.SDK.Models.Game? currentGame;

    public GameSwitcher(IPlayniteAPI api)
    {
        this.api = api;
    }

    public string? CurrentGameTitle => currentGame?.Name;

    public void SetCurrent(Playnite.SDK.Models.Game? game)
    {
        currentGame = game;
    }

    public void ClearCurrent()
    {
        currentGame = null;
    }

    public void SwitchToPlaynite()
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            var mainWindow = System.Windows.Application.Current?.MainWindow;
            var handle = IntPtr.Zero;

            if (mainWindow != null)
            {
                try
                {
                    handle = new System.Windows.Interop.WindowInteropHelper(mainWindow).Handle;
                }
                catch
                {
                    handle = IntPtr.Zero;
                }
            }

            if (handle == IntPtr.Zero)
            {
                try
                {
                    handle = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                }
                catch
                {
                    handle = IntPtr.Zero;
                }
            }

            Win32Window.RestoreAndActivate(handle);
        });
    }

    public void ExitCurrent()
    {
        if (currentGame == null)
        {
            logger.Warn("ExitCurrent called but no current game is set.");
            return;
        }

        try
        {
            // Playnite SDK doesn't expose a direct StopGame method in IPlayniteAPI
            // The game is typically stopped when the process exits or via MainViewModel
            // For now, we'll use the MainModel to stop the game if available
            var mainModel = api.MainView;
            if (mainModel != null)
            {
                // Request to close the current game through UI automation
                // This is safer than killing the process directly
                logger.Info($"Requesting to stop game: {currentGame.Name}");
                
                // Try to use the API's internal commands if exposed
                // As a fallback, we could emit a close command or use Process termination
                // For safety, we'll just log for now until we verify the correct API method
                logger.Warn("ExitCurrent functionality requires Playnite API method verification.");
                // TODO: Once API method is verified, implement: api.StopGame(currentGame.Id);
            }
            else
            {
                logger.Error("Unable to access Playnite MainView to stop game.");
            }
        }
        catch (Exception ex)
        {
            logger.Error(ex, $"Failed to exit current game: {currentGame.Name}");
        }
    }

    public IEnumerable<Playnite.SDK.Models.Game> GetRecentGames(int count)
    {
        var games = api.Database.Games.AsQueryable();
        var query = games.Where(g => g.LastActivity != null);

        if (currentGame != null)
        {
            query = query.Where(g => g.Id != currentGame.Id);
        }

        return query
            .OrderByDescending(g => g.LastActivity)
            .Take(count)
            .ToList();
    }

    public void StartGame(Guid gameId)
    {
        api.StartGame(gameId);
    }

    public string? ResolveImagePath(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return null;
        }

        if (imagePath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return imagePath;
        }

        return api.Database.GetFullFilePath(imagePath);
    }
}
