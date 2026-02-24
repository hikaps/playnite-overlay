using System;
using System.Threading.Tasks;
using Playnite.SDK;

namespace PlayniteOverlay.Services;

/// <summary>
/// Orchestrates capture functionality with automatic backend detection and fallback chains.
/// Screenshot: ShareX → SendInput
/// Recording: OBS WebSocket → SendInput
/// </summary>
public sealed class CaptureManager : IDisposable
{
    private readonly ILogger logger;
    private readonly OverlaySettings settings;
    private readonly ICaptureService? shareXService;
    private readonly ICaptureService? obsService;
    private readonly ICaptureService? sendInputService;

    private ICaptureService? screenshotService;
    private ICaptureService? recordingService;
    private bool disposed;

    /// <summary>
    /// Indicates whether screenshot functionality is available.
    /// </summary>
    public bool CanScreenshot { get; private set; }

    /// <summary>
    /// Indicates whether recording functionality is available.
    /// </summary>
    public bool CanRecord { get; private set; }

    /// <summary>
    /// Gets the current recording state from the active recording service.
    /// </summary>
    public bool IsRecording => recordingService?.IsRecording ?? false;

    /// <summary>
    /// Event raised when recording state changes.
    /// Forwarded from the active recording service.
    /// </summary>
    public event EventHandler<bool>? RecordingStateChanged;

    /// <summary>
    /// Initializes a new instance of CaptureManager with production services.
    /// </summary>
    /// <param name="settings">Overlay settings containing capture configuration.</param>
    public CaptureManager(OverlaySettings settings)
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        logger = LogManager.GetLogger();

        try
        {
            shareXService = new ShareXService();
        }
        catch (Exception ex)
        {
            logger.Debug(ex, "Failed to create ShareXService");
        }

        try
        {
            obsService = new ObsWebSocketService(settings.ObsWebSocketPassword);
        }
        catch (Exception ex)
        {
            logger.Debug(ex, "Failed to create ObsWebSocketService");
        }

        try
        {
            sendInputService = new SendInputService(settings.ScreenshotHotkey, settings.RecordHotkey);
        }
        catch (Exception ex)
        {
            logger.Debug(ex, "Failed to create SendInputService");
        }
    }

    /// <summary>
    /// Initializes a new instance of CaptureManager with injected services for testing.
    /// </summary>
    /// <param name="settings">Overlay settings containing capture configuration.</param>
    /// <param name="shareXService">ShareX service instance (or mock).</param>
    /// <param name="obsService">OBS WebSocket service instance (or mock).</param>
    /// <param name="sendInputService">SendInput service instance (or mock).</param>
    internal CaptureManager(
        OverlaySettings settings,
        ICaptureService? shareXService,
        ICaptureService? obsService,
        ICaptureService? sendInputService)
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        logger = LogManager.GetLogger();
        this.shareXService = shareXService;
        this.obsService = obsService;
        this.sendInputService = sendInputService;
    }

    /// <summary>
    /// Gets the name of the active screenshot service (for testing).
    /// </summary>
    internal string? ScreenshotServiceName => screenshotService?.Name;

    /// <summary>
    /// Gets the name of the active recording service (for testing).
    /// </summary>
    internal string? RecordingServiceName => recordingService?.Name;

    /// <summary>
    /// Detects available capture backends and sets up fallback chains.
    /// Should be called when overlay opens or settings change.
    /// </summary>
    public async Task DetectBackendsAsync()
    {
        if (!settings.EnableCapture)
        {
            CanScreenshot = false;
            CanRecord = false;
            screenshotService = null;
            recordingService = null;
            logger.Debug("Capture functionality disabled in settings");
            return;
        }

        screenshotService = await DetectScreenshotBackendAsync();
        recordingService = await DetectRecordingBackendAsync();

        CanScreenshot = screenshotService != null;
        CanRecord = recordingService != null;

        if (recordingService != null)
        {
            recordingService.RecordingStateChanged += OnRecordingStateChanged;
        }

        logger.Info($"Capture backends detected: Screenshot={screenshotService?.Name ?? "none"}, Recording={recordingService?.Name ?? "none"}");
    }

    private async Task<ICaptureService?> DetectScreenshotBackendAsync()
    {
        if (shareXService != null)
        {
            var isAvailable = await Task.Run(() => shareXService.IsAvailable()).ConfigureAwait(false);
            if (isAvailable)
            {
                logger.Debug("Using ShareX for screenshots");
                return shareXService;
            }
        }

        if (sendInputService != null)
        {
            logger.Debug("Using SendInput for screenshots (fallback)");
            return sendInputService;
        }

        logger.Debug("No screenshot backend available");
        return null;
    }

    private async Task<ICaptureService?> DetectRecordingBackendAsync()
    {
        if (obsService != null)
        {
            var isAvailable = await Task.Run(() => obsService.IsAvailable()).ConfigureAwait(false);
            if (isAvailable)
            {
                logger.Debug("Using OBS WebSocket for recording");
                return obsService;
            }
        }

        if (sendInputService != null)
        {
            logger.Debug("Using SendInput for recording (fallback)");
            return sendInputService;
        }

        logger.Debug("No recording backend available");
        return null;
    }

    /// <summary>
    /// Takes a screenshot using the active screenshot service.
    /// </summary>
    public async Task TakeScreenshotAsync()
    {
        if (screenshotService == null)
        {
            logger.Debug("Cannot take screenshot: no screenshot service available");
            return;
        }

        try
        {
            await screenshotService.TakeScreenshotAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error taking screenshot");
        }
    }

    /// <summary>
    /// Starts recording using the active recording service.
    /// </summary>
    public async Task StartRecordingAsync()
    {
        if (recordingService == null)
        {
            logger.Debug("Cannot start recording: no recording service available");
            return;
        }

        try
        {
            await recordingService.StartRecordingAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error starting recording");
        }
    }

    /// <summary>
    /// Stops recording using the active recording service.
    /// </summary>
    public async Task StopRecordingAsync()
    {
        if (recordingService == null)
        {
            logger.Debug("Cannot stop recording: no recording service available");
            return;
        }

        try
        {
            await recordingService.StopRecordingAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error stopping recording");
        }
    }

    /// <summary>
    /// Toggles recording state using the active recording service.
    /// </summary>
    public async Task ToggleRecordingAsync()
    {
        if (recordingService == null)
        {
            logger.Debug("Cannot toggle recording: no recording service available");
            return;
        }

        try
        {
            await recordingService.ToggleRecordingAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error toggling recording");
        }
    }

    private void OnRecordingStateChanged(object? sender, bool isRecording)
    {
        RecordingStateChanged?.Invoke(this, isRecording);
    }

    /// <summary>
    /// Disposes all capture services and unsubscribes from events.
    /// </summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        if (recordingService != null)
        {
            recordingService.RecordingStateChanged -= OnRecordingStateChanged;
        }

        if (obsService is IDisposable disposableObs)
        {
            disposableObs.Dispose();
        }

        disposed = true;
    }
}
