using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FFMpegCore;
using FFMpegCore.Enums;
using Playnite.SDK;
using PlayniteOverlay.Models;
namespace PlayniteOverlay.Services;

/// <summary>
/// Orchestrates screen capture and video recording functionality.
/// Combines Desktop Duplication capture with audio loopback to create screenshots and videos.
/// </summary>
public sealed class CaptureService : IDisposable
{
    private static readonly ILogger logger = LogManager.GetLogger();
    private const string NotificationSource = "CaptureService";

    private readonly IPlayniteAPI api;
    private readonly CaptureSettings settings;
    private readonly ICapture? capture;
    private AudioLoopbackCapture? audioCapture;

    private bool isRecording;
    private bool disposed;
    private string? currentRecordingPath;
    private List<string> framePaths;
    private CancellationTokenSource? recordingCts;
    private Task? recordingTask;
    private IntPtr targetMonitorHandle;

    /// <summary>
    /// Gets whether the capture service is initialized and ready.
    /// Will be false if Desktop Duplication is not supported.
    /// </summary>
    public bool IsAvailable => capture != null && capture.IsSupported;

    /// <summary>
    /// Gets whether a recording is currently in progress.
    /// </summary>
    public bool IsRecording => isRecording;

    /// <summary>
    /// Gets the last error message, if any.
    /// </summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// Initializes a new instance of the CaptureService class.
    /// Uses optional service pattern - will set IsAvailable=false if initialization fails.
    /// </summary>
    /// <param name="api">Playnite API instance for notifications.</param>
    /// <param name="settings">Capture settings configuration.</param>
    /// <param name="capture">Optional pre-configured capture implementation. If null, creates DesktopDuplicationCapture.</param>
    public CaptureService(IPlayniteAPI api, CaptureSettings settings, ICapture? capture = null)
    {
        this.api = api ?? throw new ArgumentNullException(nameof(api));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        framePaths = new List<string>();

        try
        {
            this.capture = capture ?? new DesktopDuplicationCapture();

            if (!this.capture.IsSupported)
            {
                logger.Warn("Desktop Duplication capture is not supported on this system");
                LastError = "Screen capture is not supported on this version of Windows. Requires Windows 8 or later.";
                this.capture.Dispose();
                this.capture = null;
            }
            else
            {
                audioCapture = new AudioLoopbackCapture();
                logger.Info("CaptureService initialized successfully");
            }
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to initialize CaptureService");
            LastError = $"Failed to initialize capture service: {ex.Message}";
            this.capture?.Dispose();
            this.capture = null;
        }
    }

    /// <summary>
    /// Initializes the capture for a specific monitor.
    /// Must be called before TakeScreenshot or StartRecording.
    /// </summary>
    /// <param name="monitorHandle">The handle of the monitor to capture.</param>
    public void Initialize(IntPtr monitorHandle)
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(CaptureService));
        }

        targetMonitorHandle = monitorHandle;

        if (capture == null)
        {
            logger.Warn("Cannot initialize - capture is not available");
            return;
        }

        try
        {
            capture.Initialize(monitorHandle);
            logger.Debug($"CaptureService initialized for monitor {monitorHandle}");
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to initialize capture");
            LastError = $"Failed to initialize capture: {ex.Message}";

            if (ex.Message.IndexOf("fullscreen", StringComparison.OrdinalIgnoreCase) >= 0 ||
                ex.Message.IndexOf("unsupported", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                ShowNotification("init-fullscreen", "Capture not available in exclusive fullscreen mode.", NotificationType.Error);
            }
            else
            {
                var friendlyMessage = GetUserFriendlyErrorMessage(ex, "Initialization failed.");
                ShowNotification("init-failed", $"Capture initialization failed: {friendlyMessage}", NotificationType.Error);
            }
        }
    }

    /// <summary>
    /// Captures a screenshot and saves it to the output directory.
    /// Returns the file path on success, or null on failure.
    /// </summary>
    /// <param name="outputPath">Optional custom output path. If null, uses settings.OutputPath.</param>
    /// <param name="gameName">Optional game name for filename. If null, uses timestamp only.</param>
    /// <returns>Path to saved PNG file, or null if capture failed.</returns>
    public string? TakeScreenshot(string? outputPath = null, string? gameName = null)
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(CaptureService));
        }

        if (capture == null)
        {
            LastError = "Capture service is not available";
            logger.Warn(LastError);
            ShowNotification("screenshot-unavailable", "Screenshot failed: Capture service is not available.", NotificationType.Error);
            return null;
        }

        try
        {
            var bitmap = capture.CaptureFrame();
            if (bitmap == null)
            {
                var reason = capture.LastError ?? "Failed to capture frame";
                LastError = reason;
                logger.Warn(LastError);

                if (reason.IndexOf("fullscreen", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    ShowNotification("screenshot-fullscreen", "Capture not available in exclusive fullscreen mode.", NotificationType.Error);
                }
                else
                {
                    ShowNotification("screenshot-failed", $"Screenshot failed: {reason}", NotificationType.Error);
                }
                return null;
            }

            try
            {
                var directory = GetOutputDirectory(outputPath);
                var filename = GenerateFilename(gameName, "png");
                var filePath = Path.Combine(directory, filename);

                bitmap.Save(filePath, ImageFormat.Png);
                logger.Info($"Screenshot saved to: {filePath}");
                ShowNotification("screenshot-saved", $"Screenshot saved to {filePath}", NotificationType.Info);
                return filePath;
            }
            finally
            {
                bitmap.Dispose();
            }
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error taking screenshot");
            LastError = $"Screenshot error: {ex.Message}";
            var friendlyMessage = GetUserFriendlyErrorMessage(ex, "An unexpected error occurred.");
            ShowNotification("screenshot-error", $"Screenshot failed: {friendlyMessage}", NotificationType.Error);
            return null;
        }
    }

    /// <summary>
    /// Starts recording video with audio capture.
    /// Returns true if recording started successfully.
    /// </summary>
    /// <param name="outputPath">Optional custom output path. If null, uses settings.OutputPath.</param>
    /// <param name="gameName">Optional game name for filename. If null, uses timestamp only.</param>
    /// <returns>True if recording started, false otherwise.</returns>
    public bool StartRecording(string? outputPath = null, string? gameName = null)
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(CaptureService));
        }

        if (capture == null)
        {
            LastError = "Capture service is not available";
            logger.Warn(LastError);
            ShowNotification("record-unavailable", "Recording failed: Capture service is not available.", NotificationType.Error);
            return false;
        }

        if (isRecording)
        {
            logger.Warn("Recording is already in progress");
            return true;
        }

        if (!FfmpegDetector.IsAvailable)
        {
            LastError = "FFmpeg is not installed. Please install FFmpeg to enable video recording.";
            logger.Warn(LastError);
            ShowNotification("ffmpeg-not-found", "FFmpeg not found. Please install FFmpeg to use recording.", NotificationType.Error);
            return false;
        }

        try
        {
            var directory = GetOutputDirectory(outputPath);
            var filename = GenerateFilename(gameName, "mp4");
            currentRecordingPath = Path.Combine(directory, filename);

            framePaths = new List<string>();

            if (audioCapture == null || !audioCapture.StartRecording())
            {
                LastError = "Failed to start audio capture";
                logger.Warn(LastError);
                ShowNotification("audio-failed", "Recording failed: Could not start audio capture.", NotificationType.Error);
                return false;
            }

            isRecording = true;
            recordingCts = new CancellationTokenSource();

            recordingTask = Task.Run(() => CaptureFramesLoop(recordingCts.Token));

            logger.Info($"Recording started: {currentRecordingPath}");
            ShowNotification("record-started", "Recording started", NotificationType.Info);
            return true;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error starting recording");
            LastError = $"Failed to start recording: {ex.Message}";
            CleanupRecording();
            var friendlyMessage = GetUserFriendlyErrorMessage(ex, "An unexpected error occurred.");
            ShowNotification("record-error", $"Recording failed: {friendlyMessage}", NotificationType.Error);
            return false;
        }
    }

    /// <summary>
    /// Stops the current recording and combines video frames with audio.
    /// Returns the path to the final MP4 file on success, or null on failure.
    /// </summary>
    /// <returns>Path to the MP4 file, or null if recording failed.</returns>
    public string? StopRecording()
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(CaptureService));
        }

        if (!isRecording)
        {
            logger.Debug("StopRecording called but not currently recording");
            return null;
        }

        try
        {
            recordingCts?.Cancel();

            recordingTask?.Wait(TimeSpan.FromSeconds(5));

            string? audioPath = null;
            try
            {
                audioPath = audioCapture?.SaveToTempFile();
                if (audioPath != null)
                {
                    logger.Debug($"Audio saved to temp file: {audioPath}");
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Failed to save audio");
            }

            isRecording = false;

            if (framePaths.Count == 0)
            {
                LastError = "No video frames were captured";
                logger.Warn(LastError);
                CleanupTempFiles(audioPath);
                ShowNotification("record-noframes", "Recording failed: No video frames were captured.", NotificationType.Error);
                return null;
            }

            var result = CreateVideoWithAudio(audioPath);

            CleanupTempFiles(audioPath);

            if (result != null)
            {
                ShowNotification("record-saved", $"Recording saved to {result}", NotificationType.Info);
            }
            else
            {
                var friendlyMessage = GetUserFriendlyErrorMessage(null, "Video encoding failed.");
                ShowNotification("record-failed", $"Recording failed: {friendlyMessage}", NotificationType.Error);
            }

            return result;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error stopping recording");
            LastError = $"Failed to stop recording: {ex.Message}";
            isRecording = false;
            var friendlyMessage = GetUserFriendlyErrorMessage(ex, "An unexpected error occurred.");
            ShowNotification("record-stop-error", $"Recording failed: {friendlyMessage}", NotificationType.Error);
            return null;
        }
    }

    /// <summary>
    /// Cancels the current recording without creating a video file.
    /// </summary>
    public void CancelRecording()
    {
        if (!isRecording)
        {
            return;
        }

        try
        {
            recordingCts?.Cancel();
            recordingTask?.Wait(TimeSpan.FromSeconds(2));
            audioCapture?.CancelRecording();
        }
        catch (Exception ex)
        {
            logger.Debug(ex, "Error during cancel recording");
        }
        finally
        {
            CleanupTempFiles(null);
            CleanupRecording();
        }

        logger.Info("Recording cancelled");
    }

    private void CaptureFramesLoop(CancellationToken cancellationToken)
    {
        var frameInterval = TimeSpan.FromSeconds(1.0 / 30.0);
        var tempDirectory = Path.Combine(Path.GetTempPath(), "PlayniteCapture");
        Directory.CreateDirectory(tempDirectory);

        var frameIndex = 0;
        var lastCaptureTime = DateTime.MinValue;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.UtcNow;
                var elapsed = now - lastCaptureTime;

                if (elapsed < frameInterval)
                {
                    Thread.Sleep(frameInterval - elapsed);
                }

                lastCaptureTime = DateTime.UtcNow;

                var bitmap = capture?.CaptureFrame();
                if (bitmap == null)
                {
                    continue;
                }

                try
                {
                    var framePath = Path.Combine(tempDirectory, $"frame_{frameIndex:D8}.png");
                    bitmap.Save(framePath, ImageFormat.Png);
                    framePaths.Add(framePath);
                    frameIndex++;
                }
                finally
                {
                    bitmap.Dispose();
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.Debug(ex, "Error capturing frame during recording");
            }
        }

        logger.Debug($"Captured {frameIndex} frames");
    }

    private string? CreateVideoWithAudio(string? audioPath)
    {
        if (string.IsNullOrEmpty(currentRecordingPath) || framePaths.Count == 0)
        {
            return null;
        }

        try
        {
            var tempVideoPath = Path.Combine(Path.GetTempPath(), $"playnite_video_{Guid.NewGuid():N}.mp4");

            var orderedPaths = framePaths.OrderBy(p => p).ToArray();
            logger.Debug($"Creating video from {orderedPaths.Length} frames...");

            FFMpeg.JoinImageSequence(tempVideoPath, frameRate: 30, orderedPaths);

            if (!string.IsNullOrEmpty(audioPath) && File.Exists(audioPath))
            {
                logger.Debug("Adding audio track to video...");

                FFMpegArguments
                    .FromFileInput(tempVideoPath)
                    .AddFileInput(audioPath!)
                    .OutputToFile(currentRecordingPath!, true, options => options
                        .WithVideoCodec(VideoCodec.LibX264)
                        .WithConstantRateFactor(21)
                        .WithFastStart()
                        .WithAudioCodec(AudioCodec.Aac)
                        .WithAudioBitrate(AudioQuality.Good)
                        .UsingShortest())
                    .ProcessSynchronously();

                TryDeleteFile(tempVideoPath);
            }
            else
            {
                if (File.Exists(currentRecordingPath))
                {
                    File.Delete(currentRecordingPath);
                }
                File.Move(tempVideoPath, currentRecordingPath!);
            }

            logger.Info($"Video saved to: {currentRecordingPath}");
            return currentRecordingPath;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error creating video");
            LastError = $"Failed to create video: {ex.Message}";
            return null;
        }
    }

    private string GetOutputDirectory(string? customPath)
    {
        var basePath = !string.IsNullOrWhiteSpace(customPath)
            ? customPath
            : settings.OutputPath;

        var expanded = Environment.ExpandEnvironmentVariables(basePath);

        if (!Directory.Exists(expanded))
        {
            Directory.CreateDirectory(expanded);
            logger.Debug($"Created output directory: {expanded}");
        }

        return expanded;
    }

    private string GenerateFilename(string? gameName, string extension)
    {
        var sanitizedGameName = SanitizeFilename(gameName);
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

        if (string.IsNullOrEmpty(sanitizedGameName))
        {
            return $"Capture_{timestamp}.{extension}";
        }

        return $"{sanitizedGameName}_{timestamp}.{extension}";
    }

    private static string? SanitizeFilename(string? filename)
    {
        if (string.IsNullOrWhiteSpace(filename))
        {
            return null;
        }

        var invalid = Path.GetInvalidFileNameChars();
        var result = string.Join("_", filename!.Split(invalid, StringSplitOptions.RemoveEmptyEntries));

        result = result.Trim();
        if (result.Length > 50)
        {
            result = result.Substring(0, 50);
        }

        return string.IsNullOrWhiteSpace(result) ? null : result;
    }

    private void CleanupTempFiles(string? audioPath)
    {
        if (!string.IsNullOrEmpty(audioPath))
        {
            TryDeleteFile(audioPath!);
        }

        foreach (var framePath in framePaths)
        {
            TryDeleteFile(framePath);
        }

        try
        {
            var tempDirectory = Path.Combine(Path.GetTempPath(), "PlayniteCapture");
            if (Directory.Exists(tempDirectory) && !Directory.EnumerateFileSystemEntries(tempDirectory).Any())
            {
                Directory.Delete(tempDirectory);
            }
        }
        catch
        {
        }

        framePaths.Clear();
    }

    private void CleanupRecording()
    {
        isRecording = false;
        currentRecordingPath = null;
        recordingCts?.Dispose();
        recordingCts = null;
        recordingTask = null;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private void ShowNotification(string id, string message, NotificationType type)
    {
        try
        {
            api.Notifications?.Add($"{NotificationSource}-{id}", message, type);
        }
        catch (Exception ex)
        {
            logger.Debug(ex, "Failed to show notification");
        }
    }

    private static string GetUserFriendlyErrorMessage(Exception? ex, string defaultReason)
    {
        if (ex == null)
        {
            return defaultReason;
        }

        return ex switch
        {
            UnauthorizedAccessException => "Access denied. Check folder permissions.",
            DirectoryNotFoundException => "Output folder not found.",
            IOException => "File system error occurred.",
            OutOfMemoryException => "Not enough memory to complete capture.",
            _ => defaultReason
        };
    }

    /// <summary>
    /// Releases all resources used by the CaptureService.
    /// </summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        if (isRecording)
        {
            CancelRecording();
        }

        try
        {
            capture?.Dispose();
        }
        catch (Exception ex)
        {
            logger.Debug(ex, "Error disposing capture");
        }

        try
        {
            audioCapture?.Dispose();
        }
        catch (Exception ex)
        {
            logger.Debug(ex, "Error disposing audio capture");
        }

        recordingCts?.Dispose();
        disposed = true;
        logger.Debug("CaptureService disposed");
    }
}
