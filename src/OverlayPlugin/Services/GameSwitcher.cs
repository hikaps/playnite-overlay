using System;
using System.Collections.Generic;
using System.Linq;
using Playnite.SDK;
using PlayniteOverlay;

namespace PlayniteOverlay.Services;

public sealed class GameSwitcher
{
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
        // TODO: Consider prompting Playnite to stop the running game.
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
