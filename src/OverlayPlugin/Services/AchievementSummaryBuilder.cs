using System.Collections.Generic;
using System.Linq;
using PlayniteOverlay.Models;

namespace PlayniteOverlay.Services;

/// <summary>
/// Builds <see cref="GameAchievementSummary"/> instances from achievement data lists.
/// </summary>
public static class AchievementSummaryBuilder
{
    /// <summary>
    /// Builds a summary containing total/unlocked counts, recently unlocked achievements,
    /// and a sample of locked achievements.
    /// </summary>
    /// <param name="achievements">All achievements for a game.</param>
    /// <param name="maxRecentUnlocked">Maximum number of recently unlocked achievements to include.</param>
    /// <param name="maxLocked">Maximum number of locked achievements to include.</param>
    /// <returns>A populated <see cref="GameAchievementSummary"/>.</returns>
    public static GameAchievementSummary Build(List<AchievementData> achievements, int maxRecentUnlocked, int maxLocked)
    {
        var unlocked = achievements.Where(a => a.IsUnlocked).ToList();
        var locked = achievements.Where(a => !a.IsUnlocked).ToList();

        // Get most recently unlocked (sorted by date, newest first)
        var recentlyUnlocked = unlocked
            .Where(a => a.DateUnlocked.HasValue)
            .OrderByDescending(a => a.DateUnlocked!.Value)
            .Take(maxRecentUnlocked)
            .ToList();

        // Get some locked achievements to show (just take first N for now)
        var lockedToShow = locked.Take(maxLocked).ToList();

        return new GameAchievementSummary
        {
            TotalCount = achievements.Count,
            UnlockedCount = unlocked.Count,
            RecentlyUnlocked = recentlyUnlocked,
            LockedToShow = lockedToShow
        };
    }
}
