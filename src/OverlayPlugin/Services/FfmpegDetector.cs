using System;
using System.IO;
using Playnite.SDK;

namespace PlayniteOverlay.Services;

/// <summary>
/// Detects FFmpeg availability on the system by searching PATH environment variable.
/// </summary>
public static class FfmpegDetector
{
    private static readonly ILogger logger = LogManager.GetLogger();
    private static readonly string FfmpegExecutable = "ffmpeg.exe";

    /// <summary>
    /// Gets a value indicating whether FFmpeg is available on the system.
    /// </summary>
    public static bool IsAvailable => FindFfmpeg() != null;

    /// <summary>
    /// Searches the PATH environment variable for FFmpeg executable.
    /// </summary>
    /// <returns>The full path to ffmpeg.exe if found, or null if not found.</returns>
    public static string? FindFfmpeg()
    {
        try
        {
            string? pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(pathEnv))
            {
                logger.Debug("PATH environment variable is empty or not set");
                return null;
            }

            string[] pathDirectories = pathEnv.Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries);

            logger.Debug($"Searching for {FfmpegExecutable} in {pathDirectories.Length} PATH directories");

            foreach (string directory in pathDirectories)
            {
                if (string.IsNullOrWhiteSpace(directory))
                {
                    continue;
                }

                string ffmpegPath = Path.Combine(directory, FfmpegExecutable);

                if (File.Exists(ffmpegPath))
                {
                    logger.Info($"Found FFmpeg at: {ffmpegPath}");
                    return ffmpegPath;
                }
            }

            logger.Debug($"{FfmpegExecutable} not found in PATH");
            return null;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error searching for FFmpeg in PATH");
            return null;
        }
    }
}
