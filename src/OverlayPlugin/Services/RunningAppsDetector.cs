using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using Playnite.SDK;
using PlayniteOverlay.Models;

namespace PlayniteOverlay.Services;

public sealed class RunningAppsDetector
{
    private static readonly ILogger logger = LogManager.GetLogger();
    private readonly IPlayniteAPI api;

    // System processes to exclude from detection
    private static readonly HashSet<string> SystemProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "dwm", "taskmgr", "searchhost", "startmenuexperiencehost",
        "shellexperiencehost", "runtimebroker", "applicationframehost",
        "systemsettings", "winlogon", "csrss", "services", "svchost",
        "smss", "lsass", "wininit", "fontdrvhost", "conhost", "dllhost",
        "audiodg", "spoolsv", "msiexec", "wuauclt", "backgroundtaskhost",
        "searchindexer", "searchprotocolhost", "sihost", "ctfmon",
        "taskhostw", "unsecapp", "wmiprvse", "securityhealthsystray"
    };

    // Known launcher process names (same as GameSwitcher)
    private static readonly HashSet<string> LauncherProcessNames = new(StringComparer.OrdinalIgnoreCase)
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

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    public RunningAppsDetector(IPlayniteAPI api)
    {
        this.api = api;
    }

    /// <summary>
    /// Gets all running apps that can be switched to, excluding the current game.
    /// </summary>
    /// <param name="currentGameId">Optional current game ID to exclude from results</param>
    /// <param name="showGenericApps">Whether to include non-game apps (browsers, editors, etc.)</param>
    /// <param name="maxResults">Maximum number of apps to return</param>
    /// <returns>List of running apps ordered by type (Playnite games first)</returns>
    public List<RunningApp> GetRunningApps(Guid? currentGameId = null, bool showGenericApps = true, int maxResults = 10)
    {
        var runningApps = new List<RunningApp>();

        try
        {
            // Step 1: Get Playnite-tracked running games
            var playniteGames = api.Database.Games
                .Where(g => g.IsRunning && g.Id != currentGameId)
                .ToList();

            logger.Debug($"Found {playniteGames.Count} Playnite-tracked running games");

            foreach (var game in playniteGames)
            {
                var processes = FindGameProcesses(game);
                if (processes.Any())
                {
                    var mainProcess = processes.First();
                    var app = CreatePlayniteGameApp(game, mainProcess);
                    if (app != null)
                    {
                        runningApps.Add(app);
                    }

                    // Dispose remaining processes
                    foreach (var p in processes)
                    {
                        p.Dispose();
                    }
                }
            }

            // Step 2: Get other running processes with visible windows
            var allProcesses = Process.GetProcesses();
            var playniteGamePids = new HashSet<int>(runningApps.Select(a => a.ProcessId));

            foreach (var process in allProcesses)
            {
                try
                {
                    // Skip if already added as Playnite game
                    if (playniteGamePids.Contains(process.Id))
                    {
                        process.Dispose();
                        continue;
                    }

                    // Skip if doesn't meet basic criteria
                    if (!ShouldShowInOverlay(process))
                    {
                        process.Dispose();
                        continue;
                    }

                    // Try to match to Playnite library (might be manually launched)
                    var matchedGame = TryMatchProcessToGame(process);
                    if (matchedGame != null)
                    {
                        var app = CreatePlayniteGameApp(matchedGame, process);
                        if (app != null)
                        {
                            runningApps.Add(app);
                        }
                        process.Dispose();
                        continue;
                    }

                    // Check if it looks like a game
                    if (LooksLikeGame(process))
                    {
                        var app = CreateDetectedGameApp(process);
                        if (app != null)
                        {
                            runningApps.Add(app);
                        }
                        process.Dispose();
                        continue;
                    }

                    // Generic app (if enabled)
                    if (showGenericApps)
                    {
                        var app = CreateGenericApp(process);
                        if (app != null)
                        {
                            runningApps.Add(app);
                        }
                    }

                    process.Dispose();
                }
                catch (Exception ex)
                {
                    logger.Debug(ex, $"Error processing running app: {process.ProcessName}");
                    process.Dispose();
                }
            }

            // Step 3: Sort and limit results
            // Priority: PlayniteGame > DetectedGame > GenericApp
            var sorted = runningApps
                .OrderBy(a => a.Type)
                .ThenBy(a => a.Title)
                .Take(maxResults)
                .ToList();

            logger.Info($"Detected {sorted.Count} running apps ({runningApps.Count(a => a.Type == AppType.PlayniteGame)} Playnite, {runningApps.Count(a => a.Type == AppType.DetectedGame)} detected, {runningApps.Count(a => a.Type == AppType.GenericApp)} generic)");

            return sorted;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to detect running apps");
            return new List<RunningApp>();
        }
    }

    /// <summary>
    /// Switches to (activates) the specified running app.
    /// </summary>
    public void SwitchToApp(RunningApp app)
    {
        if (app.WindowHandle == IntPtr.Zero)
        {
            logger.Warn($"Cannot switch to app: invalid window handle for {app.Title}");
            api.Notifications?.Add(
                $"switch-app-invalid-{app.ProcessId}",
                $"Cannot switch to {app.Title}: window not found",
                NotificationType.Info);
            return;
        }

        try
        {
            // Check if window still exists before trying to switch
            if (!IsWindow(app.WindowHandle))
            {
                logger.Warn($"Cannot switch to app: window no longer exists for {app.Title}");
                api.Notifications?.Add(
                    $"switch-app-gone-{app.ProcessId}",
                    $"{app.Title} is no longer running",
                    NotificationType.Info);
                return;
            }

            Win32Window.RestoreAndActivate(app.WindowHandle);
            logger.Info($"Switched to app: {app.Title} (PID: {app.ProcessId})");
        }
        catch (Exception ex)
        {
            logger.Error(ex, $"Failed to switch to app: {app.Title}");
            api.Notifications?.Add(
                $"switch-app-error-{app.ProcessId}",
                $"Failed to switch to {app.Title}",
                NotificationType.Error);
        }
    }

    private bool ShouldShowInOverlay(Process process)
    {
        try
        {
            // Must have a main window
            if (process.MainWindowHandle == IntPtr.Zero)
                return false;

            // Must have a window title
            if (string.IsNullOrWhiteSpace(process.MainWindowTitle))
                return false;

            // Window must be visible
            if (!IsWindowVisible(process.MainWindowHandle))
                return false;

            // Exclude system processes
            if (IsSystemProcess(process.ProcessName))
                return false;

            // Exclude launchers
            if (IsLauncherProcess(process.ProcessName))
                return false;

            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool IsSystemProcess(string processName)
    {
        return SystemProcessNames.Contains(processName);
    }

    private bool IsLauncherProcess(string processName)
    {
        return LauncherProcessNames.Contains(processName);
    }

    private bool LooksLikeGame(Process process)
    {
        // Heuristics to determine if process might be a game
        try
        {
            // Check for common game-related patterns in process name or window title
            var processName = process.ProcessName.ToLowerInvariant();
            var windowTitle = process.MainWindowTitle?.ToLowerInvariant() ?? "";

            // Common game executable patterns
            var gamePatterns = new[] { "game", "client", "launcher", "engine" };
            if (gamePatterns.Any(p => processName.Contains(p)))
                return true;

            // Check if it's a fullscreen or large window (games often are)
            // This is a weak heuristic but can help
            // For now, just return false to be conservative
            
            return false;
        }
        catch
        {
            return false;
        }
    }

    private Playnite.SDK.Models.Game? TryMatchProcessToGame(Process process)
    {
        try
        {
            var processName = process.ProcessName;
            var windowTitle = process.MainWindowTitle;

            // Try to find game in library that matches this process
            foreach (var game in api.Database.Games)
            {
                // Match by process name
                if (!string.IsNullOrEmpty(game.Name) &&
                    processName.IndexOf(game.Name, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    logger.Debug($"Matched process {processName} to game {game.Name} by process name");
                    return game;
                }

                // Match by window title
                if (!string.IsNullOrEmpty(windowTitle) &&
                    windowTitle.IndexOf(game.Name, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    logger.Debug($"Matched process {processName} to game {game.Name} by window title");
                    return game;
                }

                // Match by install directory
                if (!string.IsNullOrEmpty(game.InstallDirectory))
                {
                    try
                    {
                        var processPath = process.MainModule?.FileName;
                        if (!string.IsNullOrEmpty(processPath) &&
                            processPath.StartsWith(game.InstallDirectory, StringComparison.OrdinalIgnoreCase))
                        {
                            logger.Debug($"Matched process {processName} to game {game.Name} by install directory");
                            return game;
                        }
                    }
                    catch
                    {
                        // Access denied or 64-bit/32-bit mismatch
                    }
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            logger.Debug(ex, $"Error matching process to game: {process.ProcessName}");
            return null;
        }
    }

    private List<Process> FindGameProcesses(Playnite.SDK.Models.Game game)
    {
        // Reuse logic from GameSwitcher
        var matchedProcesses = new List<Process>();

        try
        {
            var allProcesses = Process.GetProcesses();
            var gameNameWords = GetGameNameWords(game.Name);
            var installDir = game.InstallDirectory;

            foreach (var process in allProcesses)
            {
                try
                {
                    if (IsLauncherProcess(process.ProcessName))
                        continue;

                    // Strategy 1: Process in install directory
                    if (!string.IsNullOrEmpty(installDir))
                    {
                        try
                        {
                            var processPath = process.MainModule?.FileName;
                            if (!string.IsNullOrEmpty(processPath) &&
                                processPath.StartsWith(installDir, StringComparison.OrdinalIgnoreCase))
                            {
                                matchedProcesses.Add(process);
                                continue;
                            }
                        }
                        catch { }
                    }

                    // Strategy 2: Fuzzy match process name
                    if (IsProcessNameMatch(process.ProcessName, gameNameWords))
                    {
                        matchedProcesses.Add(process);
                        continue;
                    }

                    // Strategy 3: Window title match
                    if (!string.IsNullOrEmpty(process.MainWindowTitle) &&
                        IsWindowTitleMatch(process.MainWindowTitle, gameNameWords))
                    {
                        matchedProcesses.Add(process);
                        continue;
                    }
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            logger.Debug(ex, $"Error finding processes for game: {game.Name}");
        }

        return matchedProcesses;
    }

    private static string[] GetGameNameWords(string gameName)
    {
        return gameName
            .Split(new[] { ' ', ':', '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2)
            .Select(w => w.ToLowerInvariant())
            .ToArray();
    }

    private static bool IsProcessNameMatch(string processName, string[] gameNameWords)
    {
        var procLower = processName.ToLowerInvariant();
        return gameNameWords.Any(word => procLower.Contains(word));
    }

    private static bool IsWindowTitleMatch(string windowTitle, string[] gameNameWords)
    {
        var titleLower = windowTitle.ToLowerInvariant();
        return gameNameWords.Count(word => titleLower.Contains(word)) >= 2;
    }

    private RunningApp? CreatePlayniteGameApp(Playnite.SDK.Models.Game game, Process process)
    {
        try
        {
            var imagePath = GetBestImagePath(game);
            var windowHandle = process.MainWindowHandle;
            var processId = process.Id;
            var title = game.Name;

            return new RunningApp
            {
                Title = title,
                ImagePath = imagePath,
                WindowHandle = windowHandle,
                GameId = game.Id,
                ProcessId = processId,
                Type = AppType.PlayniteGame,
                OnSwitch = () => SwitchToApp(new RunningApp { WindowHandle = windowHandle, Title = title, ProcessId = processId })
            };
        }
        catch (Exception ex)
        {
            logger.Debug(ex, $"Error creating Playnite game app: {game.Name}");
            return null;
        }
    }

    private RunningApp? CreateDetectedGameApp(Process process)
    {
        try
        {
            var title = !string.IsNullOrWhiteSpace(process.MainWindowTitle)
                ? process.MainWindowTitle
                : process.ProcessName;
            var windowHandle = process.MainWindowHandle;
            var processId = process.Id;

            return new RunningApp
            {
                Title = title,
                ImagePath = null,
                WindowHandle = windowHandle,
                GameId = null,
                ProcessId = processId,
                Type = AppType.DetectedGame,
                OnSwitch = () => SwitchToApp(new RunningApp { WindowHandle = windowHandle, Title = title, ProcessId = processId })
            };
        }
        catch (Exception ex)
        {
            logger.Debug(ex, $"Error creating detected game app: {process.ProcessName}");
            return null;
        }
    }

    private RunningApp? CreateGenericApp(Process process)
    {
        try
        {
            var title = !string.IsNullOrWhiteSpace(process.MainWindowTitle)
                ? process.MainWindowTitle
                : process.ProcessName;
            var windowHandle = process.MainWindowHandle;
            var processId = process.Id;

            return new RunningApp
            {
                Title = title,
                ImagePath = null,
                WindowHandle = windowHandle,
                GameId = null,
                ProcessId = processId,
                Type = AppType.GenericApp,
                OnSwitch = () => SwitchToApp(new RunningApp { WindowHandle = windowHandle, Title = title, ProcessId = processId })
            };
        }
        catch (Exception ex)
        {
            logger.Debug(ex, $"Error creating generic app: {process.ProcessName}");
            return null;
        }
    }

    private string? GetBestImagePath(Playnite.SDK.Models.Game game)
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
