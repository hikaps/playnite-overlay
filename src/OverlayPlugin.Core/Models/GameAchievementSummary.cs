using System.Collections.Generic;

namespace PlayniteOverlay.Models;

/// <summary>
/// Summary of achievements for a game, including stats and filtered lists.
/// </summary>
public sealed class GameAchievementSummary
{
    public int TotalCount { get; set; }
    public int UnlockedCount { get; set; }
    public double PercentComplete => TotalCount > 0 ? (UnlockedCount * 100.0 / TotalCount) : 0;

    /// <summary>
    /// Most recently unlocked achievements (sorted by unlock date, newest first).
    /// </summary>
    public List<AchievementData> RecentlyUnlocked { get; set; } = new List<AchievementData>();

    /// <summary>
    /// Locked achievements to display (can be random or sorted by some criteria).
    /// </summary>
    public List<AchievementData> LockedToShow { get; set; } = new List<AchievementData>();

    /// <summary>
    /// Whether any achievement data is available for display.
    /// </summary>
    public bool HasData => TotalCount > 0;
}
