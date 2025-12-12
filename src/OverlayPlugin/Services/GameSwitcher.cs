using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Playnite.SDK;
using PlayniteOverlay;

namespace PlayniteOverlay.Services;

public sealed class GameSwitcher
{
    private static readonly ILogger logger = LogManager.GetLogger();
    private readonly IPlayniteAPI api;
    private Playnite.SDK.Models.Game? currentGame;

    // Known launcher process names to exclude from termination
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

    private const int GracefulExitTimeoutMs = 3000;
    private const int ForcefulExitTimeoutMs = 1000;

    public GameSwitcher(IPlayniteAPI api)
    {
        this.api = api;
    }

    public string? CurrentGameTitle => currentGame?.Name;

    public void SetCurrent(Playnite.SDK.Models.Game? game)
    {
        currentGame = game;
    }

    public void ClearCurrent()
    {
        currentGame = null;
    }

    public void SwitchToPlaynite()
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            var mainWindow = System.Windows.Application.Current?.MainWindow;
            var handle = IntPtr.Zero;

            if (mainWindow != null)
            {
                try
                {
                    handle = new System.Windows.Interop.WindowInteropHelper(mainWindow).Handle;
                }
                catch
                {
                    handle = IntPtr.Zero;
                }
            }

            if (handle == IntPtr.Zero)
            {
                try
                {
                    handle = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                }
                catch
                {
                    handle = IntPtr.Zero;
                }
            }

            Win32Window.RestoreAndActivate(handle);
        });
    }

    public void ExitCurrent()
    {
        if (currentGame == null)
        {
            logger.Warn("ExitCurrent called but no current game is set.");
            api.Notifications?.Add(
                "exit-game-none",
                "No game is currently running.",
                NotificationType.Info);
            return;
        }

        try
        {
            logger.Info($"Attempting to exit game: {currentGame.Name}");

            // Find running processes for this game
            var processes = FindGameProcesses(currentGame).ToList();

            if (!processes.Any())
            {
                logger.Warn($"No running processes found for game: {currentGame.Name}");
                api.Notifications?.Add(
                    $"exit-game-notfound-{currentGame.Id}",
                    $"Could not find running processes for {currentGame.Name}",
                    NotificationType.Info);
                return;
            }

            logger.Info($"Found {processes.Count} process(es) for {currentGame.Name}");

            int successCount = 0;
            int failCount = 0;

            foreach (var process in processes)
            {
                try
                {
                    if (TerminateProcess(process))
                    {
                        successCount++;
                    }
                    else
                    {
                        failCount++;
                    }
                }
                catch (Exception ex)
                {
                    logger.Error(ex, $"Unexpected error terminating process: {process.ProcessName}");
                    failCount++;
                }
                finally
                {
                    process.Dispose();
                }
            }

            // Show notification based on results
            if (successCount > 0 && failCount == 0)
            {
                logger.Info($"Successfully exited all processes for {currentGame.Name}");
                api.Notifications?.Add(
                    $"exit-game-success-{currentGame.Id}",
                    $"Successfully exited {currentGame.Name}",
                    NotificationType.Info);
            }
            else if (successCount > 0 && failCount > 0)
            {
                logger.Warn($"Partially exited {currentGame.Name}: {successCount} succeeded, {failCount} failed");
                api.Notifications?.Add(
                    $"exit-game-partial-{currentGame.Id}",
                    $"Partially exited {currentGame.Name} ({successCount}/{successCount + failCount} processes)",
                    NotificationType.Info);
            }
            else
            {
                logger.Error($"Failed to exit any processes for {currentGame.Name}");
                api.Notifications?.Add(
                    $"exit-game-fail-{currentGame.Id}",
                    $"Failed to exit {currentGame.Name}. Try running Playnite as administrator.",
                    NotificationType.Error);
            }
        }
        catch (Exception ex)
        {
            logger.Error(ex, $"Failed to exit current game: {currentGame.Name}");
            api.Notifications?.Add(
                $"exit-game-error-{currentGame.Id}",
                $"Error exiting {currentGame.Name}: {ex.Message}",
                NotificationType.Error);
        }
    }

    private IEnumerable<Process> FindGameProcesses(Playnite.SDK.Models.Game game)
    {
        var matchedProcesses = new List<Process>();

        try
        {
            var allProcesses = Process.GetProcesses();
            var gameNameWords = GetGameNameWords(game.Name);
            var installDir = game.InstallDirectory;

            logger.Debug($"Searching for processes matching game: {game.Name}");
            if (!string.IsNullOrEmpty(installDir))
            {
                logger.Debug($"Game install directory: {installDir}");
            }

            foreach (var process in allProcesses)
            {
                try
                {
                    // Skip system processes and launchers
                    if (IsLauncherProcess(process.ProcessName))
                    {
                        continue;
                    }

                    // Strategy 1: Process is in game's install directory
                    if (!string.IsNullOrEmpty(installDir))
                    {
                        try
                        {
                            var processPath = process.MainModule?.FileName;
                            if (!string.IsNullOrEmpty(processPath) &&
                                processPath.StartsWith(installDir, StringComparison.OrdinalIgnoreCase))
                            {
                                logger.Debug($"Matched by install dir: {process.ProcessName} (PID: {process.Id})");
                                matchedProcesses.Add(process);
                                continue;
                            }
                        }
                        catch
                        {
                            // Access denied or 64-bit/32-bit mismatch, skip
                        }
                    }

                    // Strategy 2: Fuzzy match process name against game name
                    if (IsProcessNameMatch(process.ProcessName, gameNameWords))
                    {
                        logger.Debug($"Matched by name: {process.ProcessName} (PID: {process.Id})");
                        matchedProcesses.Add(process);
                        continue;
                    }

                    // Strategy 3: Check main window title
                    if (!string.IsNullOrEmpty(process.MainWindowTitle))
                    {
                        if (IsWindowTitleMatch(process.MainWindowTitle, gameNameWords))
                        {
                            logger.Debug($"Matched by window title: {process.ProcessName} '{process.MainWindowTitle}' (PID: {process.Id})");
                            matchedProcesses.Add(process);
                            continue;
                        }
                    }

                    // If not matched, dispose the process object
                    process.Dispose();
                }
                catch (Exception ex)
                {
                    logger.Debug(ex, $"Error checking process: {process.ProcessName}");
                    process.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error enumerating processes");
        }

        return matchedProcesses.Distinct();
    }

    private bool TerminateProcess(Process process)
    {
        try
        {
            // Check if already exited
            if (process.HasExited)
            {
                logger.Debug($"Process already exited: {process.ProcessName} (PID: {process.Id})");
                return true;
            }

            // Step 1: Try graceful close
            logger.Debug($"Sending close request to process: {process.ProcessName} (PID: {process.Id})");

            if (process.CloseMainWindow())
            {
                // Wait for graceful exit
                if (process.WaitForExit(GracefulExitTimeoutMs))
                {
                    logger.Info($"Process exited gracefully: {process.ProcessName}");
                    return true;
                }
                else
                {
                    logger.Debug($"Process did not exit within {GracefulExitTimeoutMs}ms, will force terminate");
                }
            }
            else
            {
                logger.Debug($"CloseMainWindow returned false for: {process.ProcessName}");
            }

            // Step 2: Force terminate if still running
            if (!process.HasExited)
            {
                logger.Warn($"Force terminating process: {process.ProcessName} (PID: {process.Id})");
                process.Kill();
                
                if (process.WaitForExit(ForcefulExitTimeoutMs))
                {
                    logger.Info($"Process forcefully terminated: {process.ProcessName}");
                    return true;
                }
                else
                {
                    logger.Error($"Process did not terminate even after Kill(): {process.ProcessName}");
                    return false;
                }
            }

            return true;
        }
        catch (InvalidOperationException)
        {
            // Process already exited
            logger.Debug($"Process already exited during termination: {process.ProcessName}");
            return true;
        }
        catch (Win32Exception ex)
        {
            // Access denied (UAC) or process doesn't exist
            logger.Error(ex, $"Access denied or process error: {process.ProcessName} (PID: {process.Id}). Try running Playnite as administrator.");
            return false;
        }
        catch (Exception ex)
        {
            logger.Error(ex, $"Error terminating process: {process.ProcessName}");
            return false;
        }
    }

    private bool IsLauncherProcess(string processName)
    {
        return LauncherProcessNames.Contains(processName);
    }

    private string[] GetGameNameWords(string gameName)
    {
        // Extract significant words from game name
        return gameName
            .Split(new[] { ' ', '-', '_', ':', '.', '\'', '"' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 3) // Ignore very short words
            .Select(w => w.ToLowerInvariant())
            .ToArray();
    }

    private bool IsProcessNameMatch(string processName, string[] gameNameWords)
    {
        var processLower = processName.ToLowerInvariant();

        // Check if process name contains any significant game name words
        foreach (var word in gameNameWords)
        {
            if (processLower.Contains(word))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsWindowTitleMatch(string windowTitle, string[] gameNameWords)
    {
        var titleLower = windowTitle.ToLowerInvariant();

        // Need at least 2 matching words or 1 long word (6+ chars)
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

    public IEnumerable<Playnite.SDK.Models.Game> GetRecentGames(int count)
    {
        var games = api.Database.Games.AsQueryable();
        var query = games.Where(g => g.LastActivity != null);

        if (currentGame != null)
        {
            query = query.Where(g => g.Id != currentGame.Id);
        }

        return query
            .OrderByDescending(g => g.LastActivity)
            .Take(count)
            .ToList();
    }

    public void StartGame(Guid gameId)
    {
        api.StartGame(gameId);
    }

    public string? ResolveImagePath(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return null;
        }

        if (imagePath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return imagePath;
        }

        return api.Database.GetFullFilePath(imagePath);
    }
}
