using System;

namespace PlayniteOverlay.Models;

public sealed class OverlayItem
{
    public string Title { get; set; } = string.Empty;
    public Guid GameId { get; set; }
    public string? ImagePath { get; set; }
    public string? SecondaryText { get; set; }    // "2h ago" or "Playing for 45m"
    public bool IsCurrentGame { get; set; }       // For styling/behavior
    public Action? OnSelect { get; set; }

    public static OverlayItem FromRecentGame(Playnite.SDK.Models.Game game, Services.GameSwitcher switcher)
    {
        var imagePath = GetBestImagePath(game, switcher);
        var relativeTime = switcher.GetRelativeTime(game.LastActivity);

        return new OverlayItem
        {
            Title = game.Name,
            GameId = game.Id,
            ImagePath = imagePath,
            SecondaryText = $"Last played {relativeTime}",
            IsCurrentGame = false,
            OnSelect = () => switcher.StartGame(game.Id)
        };
    }

    public static OverlayItem FromRunningApp(RunningApp app, Services.GameSwitcher switcher)
    {
        // Calculate session duration
        var sessionDuration = switcher.GetSessionDuration(app.ActivatedTime);
        
        // If it's a Playnite game, we can get metadata from the database
        if (app.Type == AppType.PlayniteGame && app.GameId.HasValue)
        {
            var game = switcher.ResolveGame(app.GameId.Value);
            if (game != null)
            {
                var imagePath = GetBestImagePath(game, switcher);
                
                return new OverlayItem
                {
                    Title = game.Name,
                    GameId = game.Id,
                    ImagePath = imagePath,
                    SecondaryText = $"Playing for {sessionDuration}",
                    IsCurrentGame = true,
                    OnSelect = null  // Can't switch to self
                };
            }
        }

        // For detected games or generic apps, show minimal info
        return new OverlayItem
        {
            Title = app.Title,
            GameId = app.GameId ?? Guid.Empty,
            ImagePath = app.ImagePath,
            SecondaryText = $"Active for {sessionDuration}",
            IsCurrentGame = true,
            OnSelect = null  // Can't switch to self
        };
    }

    private static string? GetBestImagePath(Playnite.SDK.Models.Game game, Services.GameSwitcher switcher)
    {
        // Priority: Cover → Icon → Background → null (triggers placeholder)
        var paths = new[] { game.CoverImage, game.Icon, game.BackgroundImage };
        
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            
            var resolved = switcher.ResolveImagePath(path);
            if (!string.IsNullOrWhiteSpace(resolved))
                return resolved;
        }
        
        return null; // Will trigger placeholder in UI
    }
}
