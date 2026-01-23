using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Playnite.SDK;
using PlayniteOverlay.Models;

namespace PlayniteOverlay.Services;

/// <summary>
/// Integrates with the SuccessStory plugin to retrieve achievement data.
/// </summary>
public sealed class SuccessStoryIntegration
{
    private static readonly Guid SuccessStoryPluginId = new Guid("cebe6d32-8c46-4459-b993-5a5189d60788");
    private readonly IPlayniteAPI api;
    private readonly ILogger logger;
    private bool? isAvailableCache;

    public SuccessStoryIntegration(IPlayniteAPI api)
    {
        this.api = api;
        logger = LogManager.GetLogger();
    }

    /// <summary>
    /// Checks if SuccessStory plugin is installed and available.
    /// </summary>
    public bool IsAvailable()
    {
        if (isAvailableCache.HasValue)
        {
            return isAvailableCache.Value;
        }

        try
        {
            var plugins = api.Addons.Plugins;
            isAvailableCache = plugins.Any(p => p.Id == SuccessStoryPluginId);
            logger.Debug($"SuccessStory plugin available: {isAvailableCache.Value}");
            return isAvailableCache.Value;
        }
        catch (Exception ex)
        {
            logger.Debug(ex, "Error checking SuccessStory availability");
            isAvailableCache = false;
            return false;
        }
    }

    /// <summary>
    /// Gets the achievement summary for a specific game.
    /// Returns null if SuccessStory is not available, game has no achievements, or data cannot be read.
    /// </summary>
    public GameAchievementSummary? GetGameAchievements(Guid gameId, int maxRecentUnlocked = 3, int maxLocked = 3)
    {
        if (!IsAvailable())
        {
            return null;
        }

        try
        {
            var dataPath = GetSuccessStoryDataPath(gameId);
            if (string.IsNullOrEmpty(dataPath) || !File.Exists(dataPath))
            {
                logger.Debug($"No SuccessStory data file for game {gameId}");
                return null;
            }

            var json = File.ReadAllText(dataPath);
            var achievements = ParseSuccessStoryData(json);

            if (achievements == null || achievements.Count == 0)
            {
                return null;
            }

            return BuildSummary(achievements, maxRecentUnlocked, maxLocked);
        }
        catch (Exception ex)
        {
            // Silently hide on any error (corrupted JSON, file access issues, etc.)
            logger.Debug(ex, $"Error reading SuccessStory data for game {gameId}");
            return null;
        }
    }

    private string? GetSuccessStoryDataPath(Guid gameId)
    {
        try
        {
            // Path: {ExtensionsDataPath}\cebe6d32-8c46-4459-b993-5a5189d60788\SuccessStory\{gameId}.json
            var extensionsDataPath = api.Paths.ExtensionsDataPath;
            var successStoryPath = Path.Combine(
                extensionsDataPath,
                SuccessStoryPluginId.ToString(),
                "SuccessStory",
                $"{gameId}.json");

            return successStoryPath;
        }
        catch (Exception ex)
        {
            logger.Debug(ex, "Error constructing SuccessStory data path");
            return null;
        }
    }

    private List<AchievementData>? ParseSuccessStoryData(string json)
    {
        try
        {
            // Parse JSON manually to avoid external dependencies
            // SuccessStory format: { "Items": [...], "Name": "Game Name" }
            var achievements = new List<AchievementData>();

            // Find the Items array
            var itemsStart = json.IndexOf("\"Items\"", StringComparison.OrdinalIgnoreCase);
            if (itemsStart < 0)
            {
                return null;
            }

            // Find array start
            var arrayStart = json.IndexOf('[', itemsStart);
            if (arrayStart < 0)
            {
                return null;
            }

            // Find matching array end
            var arrayEnd = FindMatchingBracket(json, arrayStart);
            if (arrayEnd < 0)
            {
                return null;
            }

            var itemsJson = json.Substring(arrayStart, arrayEnd - arrayStart + 1);

            // Parse individual achievement objects
            var objectStart = 0;
            while ((objectStart = itemsJson.IndexOf('{', objectStart)) >= 0)
            {
                var objectEnd = FindMatchingBrace(itemsJson, objectStart);
                if (objectEnd < 0)
                {
                    break;
                }

                var achievementJson = itemsJson.Substring(objectStart, objectEnd - objectStart + 1);
                var achievement = ParseAchievement(achievementJson);
                if (achievement != null)
                {
                    achievements.Add(achievement);
                }

                objectStart = objectEnd + 1;
            }

            return achievements;
        }
        catch (Exception ex)
        {
            logger.Debug(ex, "Error parsing SuccessStory JSON");
            return null;
        }
    }

    private AchievementData? ParseAchievement(string json)
    {
        try
        {
            var achievement = new AchievementData
            {
                Name = ExtractStringValue(json, "Name") ?? string.Empty,
                Description = ExtractStringValue(json, "Description") ?? string.Empty,
                UrlUnlocked = ExtractStringValue(json, "UrlUnlocked"),
                UrlLocked = ExtractStringValue(json, "UrlLocked")
            };

            var dateStr = ExtractStringValue(json, "DateUnlocked");
            if (!string.IsNullOrEmpty(dateStr) && DateTime.TryParse(dateStr, out var date))
            {
                // SuccessStory uses DateTime.MinValue (0001-01-01) for locked achievements
                // Only set DateUnlocked if it's a valid date (year > 1)
                if (date.Year > 1)
                {
                    achievement.DateUnlocked = date;
                }
            }

            return achievement;
        }
        catch
        {
            return null;
        }
    }

    private static string? ExtractStringValue(string json, string key)
    {
        var keyPattern = $"\"{key}\"";
        var keyIndex = json.IndexOf(keyPattern, StringComparison.OrdinalIgnoreCase);
        if (keyIndex < 0)
        {
            return null;
        }

        // Find the colon after the key
        var colonIndex = json.IndexOf(':', keyIndex + keyPattern.Length);
        if (colonIndex < 0)
        {
            return null;
        }

        // Skip whitespace
        var valueStart = colonIndex + 1;
        while (valueStart < json.Length && char.IsWhiteSpace(json[valueStart]))
        {
            valueStart++;
        }

        if (valueStart >= json.Length)
        {
            return null;
        }

        // Check for null
        if (json.Substring(valueStart).StartsWith("null", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // Check for string value
        if (json[valueStart] == '"')
        {
            var stringEnd = FindStringEnd(json, valueStart);
            if (stringEnd > valueStart)
            {
                return UnescapeJsonString(json.Substring(valueStart + 1, stringEnd - valueStart - 1));
            }
        }

        return null;
    }

    private static string UnescapeJsonString(string s)
    {
        return s.Replace("\\\"", "\"")
                .Replace("\\\\", "\\")
                .Replace("\\n", "\n")
                .Replace("\\r", "\r")
                .Replace("\\t", "\t");
    }

    private static int FindStringEnd(string json, int start)
    {
        if (json[start] != '"')
        {
            return -1;
        }

        for (var i = start + 1; i < json.Length; i++)
        {
            if (json[i] == '\\' && i + 1 < json.Length)
            {
                i++; // Skip escaped character
                continue;
            }
            if (json[i] == '"')
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindMatchingBracket(string json, int start)
    {
        if (json[start] != '[')
        {
            return -1;
        }

        var depth = 1;
        var inString = false;

        for (var i = start + 1; i < json.Length; i++)
        {
            var c = json[i];

            if (inString)
            {
                if (c == '\\' && i + 1 < json.Length)
                {
                    i++; // Skip escaped character
                    continue;
                }
                if (c == '"')
                {
                    inString = false;
                }
                continue;
            }

            if (c == '"')
            {
                inString = true;
            }
            else if (c == '[')
            {
                depth++;
            }
            else if (c == ']')
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    private static int FindMatchingBrace(string json, int start)
    {
        if (json[start] != '{')
        {
            return -1;
        }

        var depth = 1;
        var inString = false;

        for (var i = start + 1; i < json.Length; i++)
        {
            var c = json[i];

            if (inString)
            {
                if (c == '\\' && i + 1 < json.Length)
                {
                    i++; // Skip escaped character
                    continue;
                }
                if (c == '"')
                {
                    inString = false;
                }
                continue;
            }

            if (c == '"')
            {
                inString = true;
            }
            else if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    private GameAchievementSummary BuildSummary(List<AchievementData> achievements, int maxRecentUnlocked, int maxLocked)
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
