using System;
using PlayniteOverlay.Models;

namespace PlayniteOverlay.Services;

/// <summary>
/// Resolves the first available achievement source by checking each in priority order.
/// </summary>
public class AchievementSourceProvider
{
    private readonly IAchievementSource[] sources;
    private IAchievementSource? cachedSource;
    private bool cached;

    public AchievementSourceProvider(params IAchievementSource[] sources)
    {
        this.sources = sources ?? Array.Empty<IAchievementSource>();
    }

    /// <summary>
    /// Gets the first available achievement source, checking in priority order.
    /// Returns null if no source is available.
    /// </summary>
    public IAchievementSource? GetAvailableSource()
    {
        if (cached) return cachedSource;
        foreach (var source in sources)
        {
            try
            {
                if (source.IsAvailable())
                {
                    cachedSource = source;
                    cached = true;
                    return cachedSource;
                }
            }
            catch { /* skip failing sources */ }
        }
        cached = true;
        return null;
    }

    /// <summary>
    /// Gets achievements for a game from the first available source.
    /// </summary>
    public GameAchievementSummary? GetGameAchievements(Guid gameId, int maxRecent = 3, int maxLocked = 3)
    {
        var source = GetAvailableSource();
        return source?.GetGameAchievements(gameId, maxRecent, maxLocked);
    }
}
