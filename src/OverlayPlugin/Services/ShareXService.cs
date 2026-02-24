using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Win32;
using Playnite.SDK;

namespace PlayniteOverlay.Services;

/// <summary>
/// Implements screenshot capture via ShareX CLI.
/// ShareX is screenshot-only; recording methods are no-ops.
/// </summary>
public sealed class ShareXService : ICaptureService
{
    private readonly ILogger logger;
    private bool? isAvailableCache;
    private string? shareXPath;

    public string Name => "ShareX";

    public bool IsRecording => false;

    public event EventHandler<bool>? RecordingStateChanged;

    public ShareXService()
    {
        logger = LogManager.GetLogger();
    }

    /// <summary>
    /// Checks if ShareX is available by checking running process, registry, and common paths.
    /// Results are cached for performance.
    /// </summary>
    public bool IsAvailable()
    {
        if (isAvailableCache.HasValue)
        {
            return isAvailableCache.Value;
        }

        try
        {
            // 1. Check if ShareX process is running
            var runningProcesses = Process.GetProcessesByName("ShareX");
            if (runningProcesses.Length > 0)
            {
                // Try to get the path from the running process
                try
                {
                    shareXPath = runningProcesses[0].MainModule?.FileName;
                    if (!string.IsNullOrEmpty(shareXPath))
                    {
                        isAvailableCache = true;
                        logger.Debug($"ShareX detected via running process: {shareXPath}");
                        return true;
                    }
                }
                catch
                {
                    // Can't access process module (e.g., 32-bit vs 64-bit), continue to other checks
                }

                // Process is running but we can't get the path, still consider it available
                isAvailableCache = true;
                logger.Debug("ShareX detected via running process (path not accessible)");
                return true;
            }

            // 2. Check registry for install path

            // 2. Check registry for install path
            shareXPath = GetShareXPathFromRegistry(RegistryHive.LocalMachine) ??
                        GetShareXPathFromRegistry(RegistryHive.CurrentUser);

            if (!string.IsNullOrEmpty(shareXPath))
            {
                if (File.Exists(shareXPath))
                {
                    isAvailableCache = true;
                    logger.Debug($"ShareX detected via registry: {shareXPath}");
                    return true;
                }
            }

            // 3. Check common paths

            // 3. Check common paths
            var commonPaths = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ShareX", "ShareX.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShareX", "ShareX.exe")
            };

            foreach (var path in commonPaths)
            {
                if (File.Exists(path))
                {
                    shareXPath = path;
                    isAvailableCache = true;
                    logger.Debug($"ShareX detected via common path: {path}");
                    return true;
                }
            }

            isAvailableCache = false;
            logger.Debug("ShareX not detected");
            return false;
        }
        catch (Exception ex)
        {
            logger.Debug(ex, "Error checking ShareX availability");
            isAvailableCache = false;
            return false;
        }
    }

    /// <summary>
    /// Takes a screenshot using ShareX CLI.
    /// Uses fire-and-forget pattern; does not wait for ShareX to complete.
    /// </summary>
    public async Task TakeScreenshotAsync()
    {
        if (!IsAvailable())
        {
            logger.Debug("Cannot take screenshot: ShareX is not available");
            return;
        }

        await Task.Run(() =>
        {
            try
            {
                var exePath = shareXPath;
                if (string.IsNullOrEmpty(exePath))
                {
                    // If we don't have a cached path, try to find ShareX via process name
                    var runningProcesses = Process.GetProcessesByName("ShareX");
                    if (runningProcesses.Length > 0)
                    {
                        exePath = "ShareX.exe"; // Let Process.Start find it via PATH
                    }
                    else
                    {
                        logger.Debug("Cannot take screenshot: ShareX path not found");
                        return;
                    }
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = "-RectangleRegion -silent",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                logger.Debug($"Starting ShareX screenshot capture: {startInfo.FileName} {startInfo.Arguments}");
                Process.Start(startInfo);
                logger.Debug("ShareX screenshot capture started (fire-and-forget)");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error starting ShareX screenshot capture");
            }
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// ShareX does not support recording. Logs a debug message and returns.
    /// </summary>
    public async Task StartRecordingAsync()
    {
        await Task.Run(() =>
        {
            logger.Debug("StartRecordingAsync called on ShareX (not supported)");
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// ShareX does not support recording. Logs a debug message and returns.
    /// </summary>
    public async Task StopRecordingAsync()
    {
        await Task.Run(() =>
        {
            logger.Debug("StopRecordingAsync called on ShareX (not supported)");
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// ShareX does not support recording. Logs a debug message and returns.
    /// </summary>
    public async Task ToggleRecordingAsync()
    {
        await Task.Run(() =>
        {
            logger.Debug("ToggleRecordingAsync called on ShareX (not supported)");
        }).ConfigureAwait(false);
    }

    private string? GetShareXPathFromRegistry(RegistryHive hive)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(@"SOFTWARE\ShareX");
            if (key != null)
            {
                var path = key.GetValue("InstallPath") as string;
                if (!string.IsNullOrEmpty(path))
                {
                    var exePath = Path.Combine(path, "ShareX.exe");
                    if (File.Exists(exePath))
                    {
                        return exePath;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.Debug(ex, $"Error checking ShareX registry path in {hive}");
        }

        return null;
    }
}
