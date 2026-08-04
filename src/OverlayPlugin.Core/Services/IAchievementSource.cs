using System;
using PlayniteOverlay.Models;

namespace PlayniteOverlay.Services;

/// <summary>
/// Contract for achievement data sources that provide game achievement summaries.
/// Implementations include <see cref="SuccessStoryIntegration"/> and <see cref="PlayniteAchievementsIntegration"/>.
/// </summary>
public interface IAchievementSource
{
    /// <summary>
    /// Checks whether this achievement source is available and ready to provide data.
    /// </summary>
    /// <returns><c>true</c> if the source plugin is installed and available; otherwise, <c>false</c>.</returns>
    bool IsAvailable();

    /// <summary>
    /// Gets the achievement summary for a specific game.
    /// Returns null if the source is not available, the game has no achievements, or data cannot be read.
    /// </summary>
    /// <param name="gameId">The Playnite game identifier.</param>
    /// <param name="maxRecentUnlocked">Maximum number of recently unlocked achievements to include.</param>
    /// <param name="maxLocked">Maximum number of locked achievements to include.</param>
    /// <returns>A <see cref="GameAchievementSummary"/> if available; otherwise, <c>null</c>.</returns>
    GameAchievementSummary? GetGameAchievements(Guid gameId, int maxRecentUnlocked = 3, int maxLocked = 3);
}
