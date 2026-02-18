using System;
using NAudio.CoreAudioApi;
using Playnite.SDK;

namespace PlayniteOverlay.Services;

/// <summary>
/// Service for per-application volume control using Windows audio sessions.
/// Uses NAudio for audio session management and volume/mute control.
/// </summary>
public sealed class GameVolumeService : IDisposable
{
    private readonly ILogger logger;
    private readonly MMDeviceEnumerator? enumerator;
    private bool disposed;

    public GameVolumeService()
    {
        logger = LogManager.GetLogger();
        try
        {
            enumerator = new MMDeviceEnumerator();
            logger.Debug("GameVolumeService initialized successfully");
        }
        catch (Exception ex)
        {
            logger.Warn(ex, "Failed to initialize MMDeviceEnumerator");
            enumerator = null;
        }
    }

    /// <summary>
    /// Finds the audio session control for a given process ID.
    /// Returns null if session not found or enumeration fails.
    /// </summary>
    public AudioSessionControl? GetAudioSession(int processId)
    {
        if (enumerator == null)
        {
            logger.Debug("Enumerator not available, cannot get audio session");
            return null;
        }

        try
        {
            var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            if (device == null)
            {
                logger.Debug("Default audio device not found");
                return null;
            }

            var sessionManager = device.AudioSessionManager;
            if (sessionManager == null)
            {
                logger.Debug("Audio session manager not available");
                device.Dispose();
                return null;
            }

            var sessions = sessionManager.Sessions;
            if (sessions == null || sessions.Count == 0)
            {
                logger.Debug("No audio sessions found");
                device.Dispose();
                return null;
            }

            AudioSessionControl? matchingSession = null;
            for (int i = 0; i < sessions.Count; i++)
            {
                try
                {
                    var session = sessions[i];
                    if (session.GetProcessID == (uint)processId)
                    {
                        matchingSession = session;
                        break;
                    }
                }
                catch (Exception ex)
                {
                    logger.Debug(ex, $"Error checking session at index {i}");
                }
            }

            device.Dispose();
            logger.Debug(matchingSession != null 
                ? $"Found audio session for process {processId}" 
                : $"No audio session found for process {processId}");
            return matchingSession;
        }
        catch (Exception ex)
        {
            logger.Debug(ex, $"Error getting audio session for process {processId}");
            return null;
        }
    }

    /// <summary>
    /// Gets the current volume level for a process (0.0 to 1.0).
    /// Returns null if session not found or volume cannot be retrieved.
    /// </summary>
    public float? GetVolume(int processId)
    {
        if (processId <= 0)
        {
            logger.Warn("Invalid process ID");
            return null;
        }

        try
        {
            var session = GetAudioSession(processId);
            if (session == null)
            {
                return null;
            }

            var simpleVolume = session.SimpleAudioVolume;
            if (simpleVolume == null)
            {
                logger.Debug($"SimpleAudioVolume not available for process {processId}");
                return null;
            }

            var volume = simpleVolume.Volume;
            logger.Debug($"Volume for process {processId}: {volume}");
            return volume;
        }
        catch (Exception ex)
        {
            logger.Debug(ex, $"Error getting volume for process {processId}");
            return null;
        }
    }

    /// <summary>
    /// Sets the volume level for a process (0.0 to 1.0).
    /// Returns true if successful, false otherwise.
    /// </summary>
    public bool SetVolume(int processId, float volume)
    {
        if (processId <= 0)
        {
            logger.Warn("Invalid process ID");
            return false;
        }

        volume = Math.Max(0.0f, Math.Min(1.0f, volume));

        try
        {
            var session = GetAudioSession(processId);
            if (session == null)
            {
                return false;
            }

            var simpleVolume = session.SimpleAudioVolume;
            if (simpleVolume == null)
            {
                logger.Debug($"SimpleAudioVolume not available for process {processId}");
                return false;
            }

            simpleVolume.Volume = volume;
            logger.Debug($"Set volume for process {processId} to {volume}");
            return true;
        }
        catch (Exception ex)
        {
            logger.Debug(ex, $"Error setting volume for process {processId}");
            return false;
        }
    }

    /// <summary>
    /// Gets the mute state for a process.
    /// Returns null if session not found or mute state cannot be retrieved.
    /// </summary>
    public bool? GetMute(int processId)
    {
        if (processId <= 0)
        {
            logger.Warn("Invalid process ID");
            return null;
        }

        try
        {
            var session = GetAudioSession(processId);
            if (session == null)
            {
                return null;
            }

            var simpleVolume = session.SimpleAudioVolume;
            if (simpleVolume == null)
            {
                logger.Debug($"SimpleAudioVolume not available for process {processId}");
                return null;
            }

            var muted = simpleVolume.Mute;
            logger.Debug($"Mute state for process {processId}: {muted}");
            return muted;
        }
        catch (Exception ex)
        {
            logger.Debug(ex, $"Error getting mute state for process {processId}");
            return null;
        }
    }

    /// <summary>
    /// Sets the mute state for a process.
    /// Returns true if successful, false otherwise.
    /// </summary>
    public bool SetMute(int processId, bool mute)
    {
        if (processId <= 0)
        {
            logger.Warn("Invalid process ID");
            return false;
        }

        try
        {
            var session = GetAudioSession(processId);
            if (session == null)
            {
                return false;
            }

            var simpleVolume = session.SimpleAudioVolume;
            if (simpleVolume == null)
            {
                logger.Debug($"SimpleAudioVolume not available for process {processId}");
                return false;
            }

            simpleVolume.Mute = mute;
            logger.Debug($"Set mute for process {processId} to {mute}");
            return true;
        }
        catch (Exception ex)
        {
            logger.Debug(ex, $"Error setting mute state for process {processId}");
            return false;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        try
        {
            enumerator?.Dispose();
        }
        catch (Exception ex)
        {
            logger.Debug(ex, "Error disposing GameVolumeService");
        }

        disposed = true;
    }
}
