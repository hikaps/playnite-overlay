using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Playnite.SDK;
using PlayniteOverlay.Models;
using System.Data.SQLite;

namespace PlayniteOverlay.Services;

/// <summary>
/// Integrates with the PlayniteAchievements extension to retrieve achievement data
/// from its SQLite cache database.
/// </summary>
public sealed class PlayniteAchievementsIntegration : IAchievementSource
{
    private const string DbFileName = "achievement_cache.db";
    private const string PluginFolderName = "PlayniteAchievements";

    private readonly IPlayniteAPI api;
    private readonly ILogger logger;
    private bool? isAvailableCache;

    public PlayniteAchievementsIntegration(IPlayniteAPI api)
    {
        this.api = api;
        logger = LogManager.GetLogger();
    }

    /// <summary>
    /// Checks if the PlayniteAchievements plugin is installed and its database file exists.
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
            var pluginInstalled = plugins.Any(p =>
                p.Id.ToString().IndexOf(PluginFolderName, StringComparison.OrdinalIgnoreCase) >= 0);

            if (!pluginInstalled)
            {
                isAvailableCache = false;
                logger.Debug($"PlayniteAchievements plugin not found");
                return false;
            }

            var dbPath = GetDatabasePath();
            var dbExists = File.Exists(dbPath);
            isAvailableCache = dbExists;
            logger.Debug($"PlayniteAchievements plugin available: {isAvailableCache.Value} (DB exists: {dbExists})");
            return isAvailableCache.Value;
        }
        catch (Exception ex)
        {
            logger.Debug(ex, "Error checking PlayniteAchievements availability");
            isAvailableCache = false;
            return false;
        }
    }

    /// <summary>
    /// Gets the achievement summary for a specific game from the PlayniteAchievements database.
    /// Returns null if the source is not available, game has no achievements, or data cannot be read.
    /// </summary>
    public GameAchievementSummary? GetGameAchievements(Guid gameId, int maxRecentUnlocked = 3, int maxLocked = 3)
    {
        if (!IsAvailable())
        {
            return null;
        }

        try
        {
            var dbPath = GetDatabasePath();
            if (!File.Exists(dbPath))
            {
                logger.Debug($"PlayniteAchievements database not found at {dbPath}");
                return null;
            }

            var connectionString = $"Data Source={dbPath};Read Only=True;Version=3;";
            using (var conn = new SQLiteConnection(connectionString))
            {
                conn.Open();

                int? internalGameId = null;
                using (var cmd = new SQLiteCommand(
                    "SELECT Id FROM Games WHERE PlayniteGameId = @gameId", conn))
                {
                    cmd.Parameters.AddWithValue("@gameId", gameId.ToString());
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            internalGameId = reader.GetInt32(0);
                        }
                    }
                }

                if (!internalGameId.HasValue)
                {
                    logger.Debug($"No PlayniteAchievements game found for PlayniteGameId {gameId}");
                    return null;
                }

                var achievements = new List<AchievementData>();
                using (var cmd = new SQLiteCommand(
                    "SELECT ad.DisplayName, ad.Description, ad.UnlockedIconPath, ad.LockedIconPath, ua.Unlocked, ua.UnlockTimeUtc " +
                    "FROM AchievementDefinitions ad " +
                    "LEFT JOIN UserAchievements ua ON ua.AchievementDefinitionId = ad.Id " +
                    "WHERE ad.GameId = @internalGameId", conn))
                {
                    cmd.Parameters.AddWithValue("@internalGameId", internalGameId.Value);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var achievement = new AchievementData
                            {
                                Name = reader["DisplayName"].ToString() ?? string.Empty,
                                Description = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                                UrlUnlocked = reader.IsDBNull(2) ? null : reader.GetString(2),
                                UrlLocked = reader.IsDBNull(3) ? null : reader.GetString(3)
                            };

                            // Parse Unlocked (column 4): integer 0/1
                            if (!reader.IsDBNull(4))
                            {
                                var unlockedValue = reader.GetValue(4);
                                if (unlockedValue is long longVal && longVal == 1)
                                {
                                    achievement.IsUnlocked = true;
                                }
                                else if (unlockedValue is int intVal && intVal == 1)
                                {
                                    achievement.IsUnlocked = true;
                                }
                            }

                            // Parse UnlockTimeUtc (column 5): ISO 8601 text
                            if (!reader.IsDBNull(5) && achievement.IsUnlocked)
                            {
                                var unlockTimeStr = reader.GetString(5);
                                if (!string.IsNullOrEmpty(unlockTimeStr))
                                {
                                    DateTime parsedDate;
                                    if (DateTime.TryParse(unlockTimeStr, out parsedDate))
                                    {
                                        achievement.DateUnlocked = parsedDate;
                                    }
                                }
                            }

                            achievements.Add(achievement);
                        }
                    }
                }

                if (achievements.Count == 0)
                {
                    return null;
                }

                return AchievementSummaryBuilder.Build(achievements, maxRecentUnlocked, maxLocked);
            }
        }
        catch (Exception ex)
        {
            logger.Debug(ex, $"Error reading PlayniteAchievements data for game {gameId}");
            return null;
        }
    }

    private string GetDatabasePath()
    {
        return Path.Combine(api.Paths.ExtensionsDataPath, PluginFolderName, DbFileName);
    }
}
