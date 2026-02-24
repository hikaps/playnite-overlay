using System;
using System.Threading.Tasks;
using OBSWebsocketDotNet;
using OBSWebsocketDotNet.Types;
using Playnite.SDK;

namespace PlayniteOverlay.Services;

/// <summary>
/// Implements capture functionality via OBS WebSocket.
/// Recording-only; screenshots are not supported in this implementation.
/// </summary>
public sealed class ObsWebSocketService : ICaptureService, IDisposable
{
    private readonly OBSWebsocket obs;
    private readonly ILogger logger;
    private readonly string password;
    private bool? isAvailableCache;
    private bool isRecordingCache;
    private bool disposed;

    public string Name => "OBS WebSocket";

    public bool IsRecording => isRecordingCache;

    public event EventHandler<bool>? RecordingStateChanged;

    /// <summary>
    /// Initializes a new instance of the ObsWebSocketService.
    /// </summary>
    /// <param name="password">OBS WebSocket password. Use empty string for no authentication.</param>
    public ObsWebSocketService(string password = "")
    {
        this.password = password;
        obs = new OBSWebsocket();
        logger = LogManager.GetLogger();

        // Wire up OBS events
        obs.RecordingStateChanged += OnRecordStateChanged;
    }

    /// <summary>
    /// Checks if OBS WebSocket is available by attempting to connect.
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
            // Try to connect with 3-second timeout
            var connectTask = Task.Run(() =>
            {
                obs.Connect("ws://localhost:4455", password);
            });

            var completedTask = Task.WhenAny(connectTask, Task.Delay(3000)).Result;

            if (completedTask == connectTask && obs.IsConnected)
            {
                isAvailableCache = true;
                logger.Debug("OBS WebSocket connected and available");
                return true;
            }

            isAvailableCache = false;
            logger.Debug("OBS WebSocket connection timed out or failed");
            return false;
        }
        catch (Exception ex)
        {
            logger.Debug(ex, "Error connecting to OBS WebSocket");
            isAvailableCache = false;
            return false;
        }
    }

    /// <summary>
    /// Takes a screenshot. Not supported in OBS WebSocket implementation.
    /// Logs a debug message and returns.
    /// </summary>
    public async Task TakeScreenshotAsync()
    {
        await Task.Run(() =>
        {
            logger.Debug("TakeScreenshotAsync called on OBS WebSocket (not supported)");
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Starts OBS recording.
    /// </summary>
    public async Task StartRecordingAsync()
    {
        await Task.Run(() =>
        {
            try
            {
                if (!obs.IsConnected)
                {
                    logger.Debug("Cannot start recording: OBS WebSocket not connected");
                    return;
                }

                obs.StartRecording();
                logger.Debug("OBS recording started");
            }
            catch (Exception ex)
            {
                logger.Debug(ex, "Error starting OBS recording");
            }
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Stops OBS recording.
    /// </summary>
    public async Task StopRecordingAsync()
    {
        await Task.Run(() =>
        {
            try
            {
                if (!obs.IsConnected)
                {
                    logger.Debug("Cannot stop recording: OBS WebSocket not connected");
                    return;
                }

                obs.StopRecording();
                logger.Debug("OBS recording stopped");
            }
            catch (Exception ex)
            {
                logger.Debug(ex, "Error stopping OBS recording");
            }
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Toggles OBS recording state.
    /// </summary>
    public async Task ToggleRecordingAsync()
    {
        await Task.Run(() =>
        {
            try
            {
                if (!obs.IsConnected)
                {
                    logger.Debug("Cannot toggle recording: OBS WebSocket not connected");
                    return;
                }

                obs.ToggleRecording();
                logger.Debug("OBS recording toggled");
            }
            catch (Exception ex)
            {
                logger.Debug(ex, "Error toggling OBS recording");
            }
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Handles OBS RecordingStateChanged events.
    /// Updates cached recording state and forwards to RecordingStateChanged event.
    /// </summary>
    private void OnRecordStateChanged(object sender, OutputState state)
    {
        // OutputState is an enum: Starting, Started, Stopping, Stopped
        // We consider recording active only when in Started state
        isRecordingCache = state == OutputState.Started;
        RecordingStateChanged?.Invoke(this, isRecordingCache);
        logger.Debug($"OBS recording state changed: {state}");
    }

    /// <summary>
    /// Disposes resources and disconnects from OBS WebSocket.
    /// </summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        try
        {
            obs.RecordingStateChanged -= OnRecordStateChanged;

            if (obs.IsConnected)
            {
                obs.Disconnect();
                logger.Debug("OBS WebSocket disconnected");
            }
        }
        catch (Exception ex)
        {
            logger.Debug(ex, "Error disconnecting OBS WebSocket");
        }

        disposed = true;
    }
}
