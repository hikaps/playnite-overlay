using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using Playnite.SDK;
using PlayniteOverlay;
using PlayniteOverlay.Models;

namespace PlayniteOverlay.Services;

public sealed class GameSwitcher
{
    private static readonly ILogger logger = LogManager.GetLogger();
    private readonly IPlayniteAPI api;
    private RunningApp? activeApp;

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

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int processId);

    public GameSwitcher(IPlayniteAPI api)
    {
        this.api = api;
    }

    public RunningApp? ActiveApp => activeApp;

    public void SetActiveApp(RunningApp? app)
    {
        activeApp = app;
        if (app != null)
        {
            app.ActivatedTime = DateTime.Now;
        }
        logger.Info($"Active app changed to: {app?.Title ?? "none"}");
    }

    public void ClearActiveApp()
    {
        logger.Info($"Clearing active app: {activeApp?.Title ?? "none"}");
        activeApp = null;
    }

    public void SetActiveFromGame(Playnite.SDK.Models.Game game)
    {
        logger.Info($"Setting active app from Playnite game: {game.Name}");
        
        // Find running processes for this game
        var processes = FindGameProcesses(game);
        
        if (!processes.Any())
        {
            logger.Warn($"No running processes found for game: {game.Name}. Will retry on next overlay open.");
            // Set a placeholder that will be validated later
            activeApp = new RunningApp
            {
                Title = game.Name,
                GameId = game.Id,
                ProcessId = -1,  // Invalid PID, will be detected as invalid
                WindowHandle = IntPtr.Zero,
                Type = AppType.PlayniteGame,
                ImagePath = GetBestImagePath(game),
                ActivatedTime = DateTime.Now,
                TotalPlaytime = game.Playtime
            };
            return;
        }

        var mainProcess = processes.First();
        
        try
        {
            activeApp = new RunningApp
            {
                Title = game.Name,
                GameId = game.Id,
                ProcessId = mainProcess.Id,
                WindowHandle = mainProcess.MainWindowHandle,
                Type = AppType.PlayniteGame,
                ImagePath = GetBestImagePath(game),
                ActivatedTime = DateTime.Now,
                TotalPlaytime = game.Playtime,
                OnSwitch = null  // Current game, can't switch to self
            };
            
            logger.Debug($"Active app set: {game.Name} (PID: {mainProcess.Id})");
        }
        catch (Exception ex)
        {
            logger.Error(ex, $"Error setting active app from game: {game.Name}");
        }
        finally
        {
            // Dispose all processes
            foreach (var p in processes)
            {
                p.Dispose();
            }
        }
    }

    public bool IsActiveAppStillValid()
    {
        if (activeApp == null)
            return false;

        try
        {
            // Check if window still exists
            if (activeApp.WindowHandle != IntPtr.Zero && !IsWindow(activeApp.WindowHandle))
            {
                logger.Debug($"Active app window no longer exists: {activeApp.Title}");
                return false;
            }

            // Check if process still exists
            try
            {
                var process = Process.GetProcessById(activeApp.ProcessId);
                bool isValid = !process.HasExited;
                process.Dispose();
                
                if (!isValid)
                {
                    logger.Debug($"Active app process has exited: {activeApp.Title}");
                }
                
                return isValid;
            }
            catch (ArgumentException)
            {
                // Process no longer exists
                logger.Debug($"Active app process not found: {activeApp.Title} (PID: {activeApp.ProcessId})");
                return false;
            }
        }
        catch (Exception ex)
        {
            logger.Debug(ex, $"Error validating active app: {activeApp.Title}");
            return false;
        }
    }

    public RunningApp? DetectForegroundApp(bool includeGenericApps, int maxApps = 10)
    {
        try
        {
            IntPtr foregroundWindow = GetForegroundWindow();
            if (foregroundWindow == IntPtr.Zero)
            {
                logger.Debug("No foreground window detected");
                return null;
            }

            // Get process ID from foreground window
            GetWindowThreadProcessId(foregroundWindow, out int foregroundProcessId);
            
            if (foregroundProcessId == 0)
            {
                logger.Debug("Could not get process ID for foreground window");
                return null;
            }

            // Check if foreground window is Playnite itself
            try
            {
                var currentProcess = Process.GetCurrentProcess();
                if (foregroundProcessId == currentProcess.Id)
                {
                    logger.Debug("Foreground window is Playnite, ignoring");
                    currentProcess.Dispose();
                    return null;
                }
                currentProcess.Dispose();
            }
            catch { }

            // Get all running apps and find the one that matches foreground window
            var runningApps = new RunningAppsDetector(api).GetRunningApps(
                activeApp?.GameId, 
                includeGenericApps, 
                maxApps);

            // Try to find exact match by window handle first
            var matchByWindow = runningApps.FirstOrDefault(app => 
                app.WindowHandle == foregroundWindow);
            
            if (matchByWindow != null)
            {
                logger.Debug($"Auto-detected foreground app by window handle: {matchByWindow.Title} (PID: {matchByWindow.ProcessId})");
                return matchByWindow;
            }

            // Try to find match by process ID
            var matchByProcess = runningApps.FirstOrDefault(app => 
                app.ProcessId == foregroundProcessId);
            
            if (matchByProcess != null)
            {
                logger.Debug($"Auto-detected foreground app by process ID: {matchByProcess.Title} (PID: {matchByProcess.ProcessId})");
                return matchByProcess;
            }

            logger.Debug($"Foreground window (PID: {foregroundProcessId}) not found in running apps list");
            return null;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error detecting foreground app");
            return null;
        }
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

    public void ExitActiveApp()
    {
        if (activeApp == null)
        {
            logger.Warn("ExitActiveApp called but no active app is set.");
            api.Notifications?.Add(
                "exit-app-none",
                "No app is currently active.",
                NotificationType.Info);
            return;
        }

        try
        {
            logger.Info($"Attempting to exit active app: {activeApp.Title}");

            // Strategy depends on AppType
            if (activeApp.Type == AppType.PlayniteGame && activeApp.GameId.HasValue)
            {
                // Use Playnite's game exit logic (graceful + forceful)
                var game = api.Database.Games[activeApp.GameId.Value];
                if (game != null)
                {
                    logger.Debug($"Active app is Playnite game, using game termination logic");
                    
                    // Find and terminate game processes
                    var processes = FindGameProcesses(game).ToList();
                    
                    if (!processes.Any())
                    {
                        logger.Warn($"No running processes found for game: {game.Name}");
                        api.Notifications?.Add(
                            $"exit-game-notfound-{game.Id}",
                            $"Could not find running processes for {game.Name}",
                            NotificationType.Info);
                        return;
                    }

                    logger.Info($"Found {processes.Count} process(es) for {game.Name}");

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
                        logger.Info($"Successfully exited all processes for {game.Name}");
                        api.Notifications?.Add(
                            $"exit-game-success-{game.Id}",
                            $"Successfully exited {game.Name}",
                            NotificationType.Info);
                    }
                    else if (successCount > 0 && failCount > 0)
                    {
                        logger.Warn($"Partially exited {game.Name}: {successCount} succeeded, {failCount} failed");
                        api.Notifications?.Add(
                            $"exit-game-partial-{game.Id}",
                            $"Partially exited {game.Name} ({successCount}/{successCount + failCount} processes)",
                            NotificationType.Info);
                    }
                    else
                    {
                        logger.Error($"Failed to exit any processes for {game.Name}");
                        api.Notifications?.Add(
                            $"exit-game-fail-{game.Id}",
                            $"Failed to exit {game.Name}. Try running Playnite as administrator.",
                            NotificationType.Error);
                    }
                    return;
                }
            }

            // For non-Playnite games or generic apps, terminate by process ID
            try
            {
                var process = Process.GetProcessById(activeApp.ProcessId);
                
                if (TerminateProcess(process))
                {
                    logger.Info($"Successfully exited active app: {activeApp.Title}");
                    api.Notifications?.Add(
                        $"exit-app-success-{activeApp.ProcessId}",
                        $"Successfully exited {activeApp.Title}",
                        NotificationType.Info);
                }
                else
                {
                    logger.Error($"Failed to exit active app: {activeApp.Title}");
                    api.Notifications?.Add(
                        $"exit-app-fail-{activeApp.ProcessId}",
                        $"Failed to exit {activeApp.Title}. Try running Playnite as administrator.",
                        NotificationType.Error);
                }
                
                process.Dispose();
            }
            catch (ArgumentException)
            {
                // Process not found - already exited
                logger.Info($"Active app already exited: {activeApp.Title}");
                api.Notifications?.Add(
                    $"exit-app-gone-{activeApp.ProcessId}",
                    $"{activeApp.Title} is no longer running",
                    NotificationType.Info);
            }
        }
        catch (Exception ex)
        {
            logger.Error(ex, $"Failed to exit active app: {activeApp.Title}");
            api.Notifications?.Add(
                $"exit-app-error-{activeApp.ProcessId}",
                $"Error exiting {activeApp.Title}: {ex.Message}",
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

        // Exclude active app if it's a Playnite game
        if (activeApp?.GameId != null)
        {
            query = query.Where(g => g.Id != activeApp.GameId.Value);
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

    public string GetRelativeTime(DateTime? dateTime)
    {
        if (!dateTime.HasValue) return "never";
        
        var span = DateTime.Now - dateTime.Value;
        
        if (span.TotalMinutes < 1) return "just now";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours}h ago";
        if (span.TotalDays < 2) return "yesterday";
        if (span.TotalDays < 7) return $"{(int)span.TotalDays}d ago";
        if (span.TotalDays < 30) return $"{(int)span.TotalDays / 7}w ago";
        if (span.TotalDays < 365) return $"{(int)span.TotalDays / 30}mo ago";
        
        // For > 1 year, show abbreviated date
        if (dateTime.Value.Year == DateTime.Now.Year - 1)
            return "last year";
        
        return dateTime.Value.ToString("MMM yyyy");
    }

    public string GetSessionDuration(DateTime? startTime)
    {
        if (!startTime.HasValue) return "0m";
        
        var duration = DateTime.Now - startTime.Value;
        
        if (duration.TotalMinutes < 1) return "< 1m";
        if (duration.TotalHours < 1) return $"{(int)duration.TotalMinutes}m";
        if (duration.TotalHours < 24) 
        {
            var hours = duration.Hours;
            var minutes = duration.Minutes;
            if (minutes == 0) return $"{hours}h";
            return $"{hours}h {minutes}m";
        }
        
        return $"{(int)duration.TotalHours}h";
    }

    public string FormatPlaytime(ulong seconds)
    {
        if (seconds == 0) return "Not played";
        
        var hours = (double)seconds / 3600.0;
        
        if (hours < 1) return "< 1 hour";
        if (hours < 100) return $"{hours:F1} hours";
        
        return $"{hours:N0} hours"; // e.g., "1,234 hours"
    }

    public Playnite.SDK.Models.Game? ResolveGame(Guid gameId)
    {
        return api.Database.Games[gameId];
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
