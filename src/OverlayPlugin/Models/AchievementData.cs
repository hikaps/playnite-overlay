using System;

namespace PlayniteOverlay.Models;

/// <summary>
/// Represents a single achievement from SuccessStory plugin.
/// </summary>
public sealed class AchievementData
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime? DateUnlocked { get; set; }
    public string? UrlUnlocked { get; set; }
    public string? UrlLocked { get; set; }

    public bool IsUnlocked => DateUnlocked.HasValue;
}
