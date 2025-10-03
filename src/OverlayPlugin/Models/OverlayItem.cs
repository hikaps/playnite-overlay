using System;

namespace PlayniteOverlay.Models;

public sealed class OverlayItem
{
    public string Title { get; set; } = string.Empty;
    public Guid GameId { get; set; }
    public string? ImagePath { get; set; }
    public Action? OnSelect { get; set; }

    public static OverlayItem FromGame(Playnite.SDK.Models.Game game, Services.GameSwitcher switcher)
    {
        var imagePath = string.IsNullOrWhiteSpace(game.CoverImage) ? game.Icon : game.CoverImage;

        return new OverlayItem
        {
            Title = game.Name,
            GameId = game.Id,
            ImagePath = switcher.ResolveImagePath(imagePath),
            OnSelect = () => switcher.StartGame(game.Id)
        };
    }
}
