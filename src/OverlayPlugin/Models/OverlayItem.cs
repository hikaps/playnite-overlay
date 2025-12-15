using System;

namespace PlayniteOverlay.Models;

public sealed class OverlayItem
{
    public string Title { get; set; } = string.Empty;
    public Guid GameId { get; set; }
    public string? ImagePath { get; set; }
    public string? SecondaryText { get; set; }    // "2h ago" or "Playing for 45m"
    public string? TertiaryText { get; set; }     // "12.5 hours total"
    public bool IsCurrentGame { get; set; }       // For styling/behavior
    public Action? OnSelect { get; set; }

    public static OverlayItem FromCurrentGame(Playnite.SDK.Models.Game game, Services.GameSwitcher switcher)
    {
        var imagePath = GetBestImagePath(game, switcher);
        var sessionDuration = switcher.GetSessionDuration(switcher.CurrentGameStartTime);
        var totalPlaytime = switcher.FormatPlaytime(game.Playtime);

        return new OverlayItem
        {
            Title = game.Name,
            GameId = game.Id,
            ImagePath = imagePath,
            SecondaryText = $"Playing for {sessionDuration}",
            TertiaryText = totalPlaytime,
            IsCurrentGame = true,
            OnSelect = null  // Can't switch to self
        };
    }

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

    public static OverlayItem FromGame(Playnite.SDK.Models.Game game, Services.GameSwitcher switcher)
    {
        // Backwards compatibility - delegates to FromRecentGame
        return FromRecentGame(game, switcher);
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
