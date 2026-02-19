using System;
using System.IO;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Playnite.SDK;

namespace PlayniteOverlay.Services;

/// <summary>
/// Captures system audio (loopback) - what's playing through speakers.
/// Uses NAudio WasapiLoopbackCapture to record audio output from the default device.
/// Buffered in memory during recording and can be retrieved as WAV data.
/// </summary>
public sealed class AudioLoopbackCapture : IDisposable
{
    private readonly ILogger logger;
    private WasapiLoopbackCapture? capture;
    private MemoryStream? audioBuffer;
    private WaveFileWriter? waveWriter;
    private bool isRecording;
    private bool disposed;
    private bool deviceDisconnected;

    /// <summary>
    /// Gets whether audio capture is currently recording.
    /// </summary>
    public bool IsRecording => isRecording;

    /// <summary>
    /// Gets whether a device disconnection occurred during recording.
    /// </summary>
    public bool DeviceDisconnected => deviceDisconnected;

    /// <summary>
    /// Gets the last error message, if any.
    /// </summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// Initializes a new instance of the AudioLoopbackCapture class.
    /// </summary>
    public AudioLoopbackCapture()
    {
        logger = LogManager.GetLogger();
        logger.Debug("AudioLoopbackCapture initialized");
    }

    /// <summary>
    /// Starts capturing system audio from the default output device.
    /// Audio is buffered in memory until StopRecording is called.
    /// Returns true if recording started successfully.
    /// </summary>
    public bool StartRecording()
    {
        if (isRecording)
        {
            logger.Warn("StartRecording called while already recording");
            return true;
        }

        if (disposed)
        {
            logger.Warn("Cannot start recording on disposed instance");
            LastError = "Capture instance has been disposed";
            return false;
        }

        try
        {
            // Get default audio output device
            using var enumerator = new MMDeviceEnumerator();
            var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);

            if (defaultDevice == null)
            {
                logger.Warn("No default audio output device found");
                LastError = "No default audio output device available";
                return false;
            }

            // Create loopback capture for the default device
            capture = new WasapiLoopbackCapture(defaultDevice);

            // Set up event handlers
            capture.DataAvailable += OnDataAvailable;
            capture.RecordingStopped += OnRecordingStopped;

            // Initialize memory buffer and wave writer
            audioBuffer = new MemoryStream();
            waveWriter = new WaveFileWriter(audioBuffer, capture.WaveFormat);

            // Reset state
            deviceDisconnected = false;
            LastError = null;
            isRecording = true;

            // Start capturing
            capture.StartRecording();
            logger.Info($"Started audio loopback capture (Format: {capture.WaveFormat})");
            return true;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to start audio loopback capture");
            LastError = $"Failed to start capture: {ex.Message}";
            CleanupCapture();
            return false;
        }
    }

    /// <summary>
    /// Stops capturing audio and returns the recorded audio as a WAV byte array.
    /// Returns null if recording failed or no audio was captured.
    /// </summary>
    /// <returns>WAV file data as byte array, or null if capture failed.</returns>
    public byte[]? StopRecording()
    {
        if (!isRecording || capture == null)
        {
            logger.Debug("StopRecording called but not currently recording");
            return GetWavData();
        }

        try
        {
            // Stop the capture
            capture.StopRecording();

            // Wait for recording to fully stop (with timeout)
            var timeout = DateTime.Now.AddSeconds(5);
            while (isRecording && DateTime.Now < timeout)
            {
                System.Threading.Thread.Sleep(50);
            }

            return GetWavData();
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error stopping audio capture");
            LastError = $"Error stopping capture: {ex.Message}";
            return null;
        }
        finally
        {
            CleanupCapture();
        }
    }

    /// <summary>
    /// Gets the captured audio as a WAV byte array without stopping the recording.
    /// Returns null if no audio has been captured or if capture failed.
    /// Note: This returns a snapshot of current buffer; recording continues.
    /// </summary>
    /// <returns>WAV file data as byte array, or null if no audio available.</returns>
    public byte[]? GetWavData()
    {
        if (waveWriter == null || audioBuffer == null)
        {
            logger.Debug("GetWavData called but no audio buffer available");
            return null;
        }

        try
        {
            // Flush the wave writer to ensure all data is in the buffer
            waveWriter.Flush();

            // Get the WAV data from the buffer
            if (audioBuffer.Length == 0)
            {
                logger.Debug("Audio buffer is empty");
                return null;
            }

            // The WaveFileWriter writes the WAV header at the beginning
            // but the length fields in the header need to be updated
            // We need to create a proper copy with corrected headers

            var wavData = audioBuffer.ToArray();

            // Update the WAV header with correct lengths
            if (wavData.Length > 44)
            {
                UpdateWavHeader(wavData);
            }

            logger.Debug($"GetWavData returning {wavData.Length} bytes");
            return wavData;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error getting WAV data");
            LastError = $"Error getting WAV data: {ex.Message}";
            return null;
        }
    }

    /// <summary>
    /// Saves the captured audio to a temporary WAV file.
    /// Returns the path to the temp file, or null if save failed.
    /// Caller is responsible for deleting the temp file when done.
    /// </summary>
    /// <returns>Path to temp WAV file, or null if save failed.</returns>
    public string? SaveToTempFile()
    {
        var wavData = GetWavData();
        if (wavData == null)
        {
            return null;
        }

        try
        {
            var tempPath = Path.Combine(Path.GetTempPath(), $"playnite_audio_{Guid.NewGuid():N}.wav");
            File.WriteAllBytes(tempPath, wavData);
            logger.Debug($"Saved audio to temp file: {tempPath}");
            return tempPath;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error saving audio to temp file");
            LastError = $"Error saving to temp file: {ex.Message}";
            return null;
        }
    }

    /// <summary>
    /// Cancels the current recording without returning audio data.
    /// </summary>
    public void CancelRecording()
    {
        if (!isRecording)
        {
            return;
        }

        try
        {
            capture?.StopRecording();
        }
        catch (Exception ex)
        {
            logger.Debug(ex, "Error during cancel recording");
        }
        finally
        {
            CleanupCapture();
        }

        logger.Info("Audio capture cancelled");
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (waveWriter != null && e.BytesRecorded > 0)
        {
            try
            {
                waveWriter.Write(e.Buffer, 0, e.BytesRecorded);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error writing audio data to buffer");
            }
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        isRecording = false;

        if (e.Exception != null)
        {
            logger.Error(e.Exception, "Audio recording stopped due to error");

            // Check if this is a device disconnection
            if (e.Exception is COMException ||
                e.Exception.Message.IndexOf("device", StringComparison.OrdinalIgnoreCase) >= 0 ||
                e.Exception.Message.IndexOf("disconnect", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                deviceDisconnected = true;
                LastError = "Audio device was disconnected during recording";
            }
            else
            {
                LastError = $"Recording stopped due to error: {e.Exception.Message}";
            }
        }
        else
        {
            logger.Debug("Audio recording stopped normally");
        }
    }

    private void UpdateWavHeader(byte[] wavData)
    {
        // WAV file structure:
        // Bytes 4-7: File size - 8 (little endian)
        // Bytes 40-43: Data chunk size (little endian)

        try
        {
            var fileSize = wavData.Length - 8;
            var dataSize = wavData.Length - 44; // Subtract header size

            // Update RIFF chunk size (bytes 4-7)
            wavData[4] = (byte)(fileSize & 0xFF);
            wavData[5] = (byte)((fileSize >> 8) & 0xFF);
            wavData[6] = (byte)((fileSize >> 16) & 0xFF);
            wavData[7] = (byte)((fileSize >> 24) & 0xFF);

            // Update data chunk size (bytes 40-43)
            wavData[40] = (byte)(dataSize & 0xFF);
            wavData[41] = (byte)((dataSize >> 8) & 0xFF);
            wavData[42] = (byte)((dataSize >> 16) & 0xFF);
            wavData[43] = (byte)((dataSize >> 24) & 0xFF);
        }
        catch (Exception ex)
        {
            logger.Debug(ex, "Error updating WAV header");
        }
    }

    private void CleanupCapture()
    {
        try
        {
            waveWriter?.Dispose();
        }
        catch (Exception ex)
        {
            logger.Debug(ex, "Error disposing wave writer");
        }
        waveWriter = null;

        try
        {
            audioBuffer?.Dispose();
        }
        catch (Exception ex)
        {
            logger.Debug(ex, "Error disposing audio buffer");
        }
        audioBuffer = null;

        try
        {
            if (capture != null)
            {
                capture.DataAvailable -= OnDataAvailable;
                capture.RecordingStopped -= OnRecordingStopped;
                capture.Dispose();
            }
        }
        catch (Exception ex)
        {
            logger.Debug(ex, "Error disposing capture");
        }
        capture = null;
        isRecording = false;
    }

    /// <summary>
    /// Releases all resources used by the AudioLoopbackCapture.
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

        CleanupCapture();
        disposed = true;
        logger.Debug("AudioLoopbackCapture disposed");
    }
}
