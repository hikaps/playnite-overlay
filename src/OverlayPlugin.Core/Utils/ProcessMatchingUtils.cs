using System;
using System.Collections.Generic;
using System.Linq;
using Playnite.SDK;

namespace PlayniteOverlay.Utils;

/// <summary>
/// Shared utilities for matching processes to games and resolving game images.
/// </summary>
internal static class ProcessMatchingUtils
{
    /// <summary>
    /// Known launcher process names to exclude from game matching.
    /// </summary>
    public static readonly HashSet<string> LauncherProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "steam", "steamservice", "steamwebhelper",
        "epicgameslauncher", "epicwebhelper", "eossdk-win64-shipping",
        "upc", "uplay", "ubisoftconnect",
        "origin", "originwebhelperservice",
        "battlenet", "battle.net", "blizzard",
        "bethesdanetlauncher", "bethesda.net",
        "amazongames", "amazongameslauncher",
        "gog", "galaxyclient", "galaxyclientservice",
        "rockstargameslauncher", "socialclub",
        "playnite", "playnite.desktopapp", "playnite.fullscreenapp"
    };

    /// <summary>
    /// Checks if the given process name is a known launcher.
    /// </summary>
    public static bool IsLauncherProcess(string processName)
    {
        return LauncherProcessNames.Contains(processName);
    }

    /// <summary>
    /// Extracts significant words from a game name for fuzzy matching.
    /// </summary>
    public static string[] GetGameNameWords(string gameName)
    {
        return gameName
            .Split(new[] { ' ', '-', '_', ':', '.', '\'', '"' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 3) // Ignore very short words
            .Select(w => w.ToLowerInvariant())
            .ToArray();
    }

    /// <summary>
    /// Checks if a process name matches any of the game name words.
    /// </summary>
    public static bool IsProcessNameMatch(string processName, string[] gameNameWords)
    {
        var processLower = processName.ToLowerInvariant();
        return gameNameWords.Any(word => processLower.Contains(word));
    }

    /// <summary>
    /// Checks if a window title matches the game name words.
    /// Requires at least 2 matching words or 1 long word (6+ chars).
    /// </summary>
    public static bool IsWindowTitleMatch(string windowTitle, string[] gameNameWords)
    {
        var titleLower = windowTitle.ToLowerInvariant();
        int matchCount = 0;

        foreach (var word in gameNameWords)
        {
            if (titleLower.Contains(word))
            {
                if (word.Length >= 6)
                {
                    return true; // Single distinctive word match
                }
                matchCount++;
            }
        }

        return matchCount >= 2;
    }

    /// <summary>
    /// Gets the best available image path for a game (cover > icon > background).
    /// </summary>
    public static string? GetBestImagePath(Playnite.SDK.Models.Game game, IPlayniteAPI api)
    {
        var paths = new[] { game.CoverImage, game.Icon, game.BackgroundImage };

        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;

            try
            {
                var resolved = api.Database.GetFullFilePath(path);
                if (!string.IsNullOrWhiteSpace(resolved))
                    return resolved;
            }
            catch { }
        }

        return null;
    }
}
