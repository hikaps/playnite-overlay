using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Playnite.SDK;
using PlayniteOverlay.Models;

namespace PlayniteOverlay.Services;

/// <summary>
/// Encodes video and audio to MP4 using Windows Media Foundation.
/// Supports H.264 video encoding and AAC audio encoding.
/// </summary>
internal sealed class MediaFoundationEncoder : IDisposable
{
    private static readonly ILogger logger = LogManager.GetLogger();

    private const int BitrateLow = 2_000_000;
    private const int BitrateMedium = 5_000_000;
    private const int BitrateHigh = 10_000_000;
    private const int FrameRateNumerator = 30;
    private const int FrameRateDenominator = 1;
    private const int AudioSampleRate = 44100;
    private const int AudioChannels = 2;
    private const int AudioBitsPerSample = 16;
    private const int AudioBitrate = 192_000;
    private const long FrameDuration100Ns = 10_000_000L / FrameRateNumerator;

    private readonly int videoWidth;
    private readonly int videoHeight;
    private readonly int videoBitrate;

    private MediaFoundation.IMFSinkWriter? sinkWriter;
    private int videoStreamIndex = -1;
    private int audioStreamIndex = -1;

    private long videoTimestamp;
    private long audioTimestamp;
    private readonly long audioBytesPerSecond;
    private bool isFinalized;
    private bool disposed;
    private bool mfInitialized;

    public bool IsInitialized { get; private set; }

    public string? LastError { get; private set; }

    public MediaFoundationEncoder(string outputPath, int width, int height, VideoQuality quality)
    {
        if (string.IsNullOrEmpty(outputPath))
        {
            throw new ArgumentNullException(nameof(outputPath));
        }

        if (width <= 0 || height <= 0)
        {
            throw new ArgumentException("Video dimensions must be positive", $"{nameof(width)}, {nameof(height)}");
        }

        videoWidth = width;
        videoHeight = height;
        videoBitrate = GetBitrateForQuality(quality);
        audioBytesPerSecond = AudioSampleRate * AudioChannels * (AudioBitsPerSample / 8);

        Initialize(outputPath);
    }

    private static int GetBitrateForQuality(VideoQuality quality)
    {
        return quality switch
        {
            VideoQuality.Low => BitrateLow,
            VideoQuality.Medium => BitrateMedium,
            VideoQuality.High => BitrateHigh,
            _ => BitrateMedium
        };
    }

    private void Initialize(string outputPath)
    {
        try
        {
            var hr = MediaFoundation.MFStartup(MediaFoundation.MF_VERSION, 0);
            if (hr < 0)
            {
                LastError = $"MFStartup failed with HRESULT 0x{hr:X8}";
                logger.Error(LastError);
                return;
            }
            mfInitialized = true;

            hr = MediaFoundation.MFCreateSinkWriterFromURL(outputPath, IntPtr.Zero, IntPtr.Zero, out sinkWriter);
            if (hr < 0)
            {
                LastError = $"MFCreateSinkWriterFromURL failed with HRESULT 0x{hr:X8}";
                logger.Error(LastError);
                Cleanup();
                return;
            }

            if (!ConfigureVideoStream())
            {
                Cleanup();
                return;
            }

            if (!ConfigureAudioStream())
            {
                Cleanup();
                return;
            }

            hr = sinkWriter.BeginWriting();
            if (hr < 0)
            {
                LastError = $"BeginWriting failed with HRESULT 0x{hr:X8}";
                logger.Error(LastError);
                Cleanup();
                return;
            }

            IsInitialized = true;
            logger.Info($"MediaFoundationEncoder initialized: {videoWidth}x{videoHeight}, {videoBitrate / 1_000_000} Mbps");
        }
        catch (Exception ex)
        {
            LastError = $"Initialization failed: {ex.Message}";
            logger.Error(ex, "Failed to initialize MediaFoundationEncoder");
            Cleanup();
        }
    }

    private bool ConfigureVideoStream()
    {
        try
        {
            var hr = MediaFoundation.MFCreateMediaType(out var outputType);
            if (hr < 0)
            {
                LastError = $"MFCreateMediaType for video output failed with HRESULT 0x{hr:X8}";
                logger.Error(LastError);
                return false;
            }

            try
            {
                var attrKey = MediaFoundation.MF_MT_MAJOR_TYPE;
                var majorType = MediaFoundation.MFMediaType_Video;
                hr = outputType.SetGUID(ref attrKey, ref majorType);
                if (hr < 0) { LastError = "Failed to set video major type"; return false; }

                attrKey = MediaFoundation.MF_MT_SUBTYPE;
                var subType = MediaFoundation.MFVideoFormat_H264;
                hr = outputType.SetGUID(ref attrKey, ref subType);
                if (hr < 0) { LastError = "Failed to set H.264 subtype"; return false; }

                attrKey = MediaFoundation.MF_MT_AVG_BITRATE;
                hr = outputType.SetUINT32(ref attrKey, videoBitrate);
                if (hr < 0) { LastError = "Failed to set video bitrate"; return false; }

                attrKey = MediaFoundation.MF_MT_INTERLACE_MODE;
                hr = outputType.SetUINT32(ref attrKey, MediaFoundation.MFVideoInterlace_Progressive);
                if (hr < 0) { LastError = "Failed to set interlace mode"; return false; }

                attrKey = MediaFoundation.MF_MT_FRAME_SIZE;
                var frameSizeValue = ((long)videoWidth << 32) | (long)videoHeight;
                hr = outputType.SetUINT64(ref attrKey, frameSizeValue);
                if (hr < 0) { LastError = "Failed to set frame size"; return false; }

                attrKey = MediaFoundation.MF_MT_FRAME_RATE;
                var frameRateValue = ((long)FrameRateNumerator << 32) | (long)FrameRateDenominator;
                hr = outputType.SetUINT64(ref attrKey, frameRateValue);
                if (hr < 0) { LastError = "Failed to set frame rate"; return false; }

                hr = sinkWriter!.AddStream(outputType, out videoStreamIndex);
                if (hr < 0)
                {
                    LastError = $"AddStream for video failed with HRESULT 0x{hr:X8}";
                    logger.Error(LastError);
                    return false;
                }
            }
            finally
            {
                Marshal.ReleaseComObject(outputType);
            }

            hr = MediaFoundation.MFCreateMediaType(out var inputType);
            if (hr < 0)
            {
                LastError = $"MFCreateMediaType for video input failed with HRESULT 0x{hr:X8}";
                logger.Error(LastError);
                return false;
            }

            try
            {
                var attrKey = MediaFoundation.MF_MT_MAJOR_TYPE;
                var majorType = MediaFoundation.MFMediaType_Video;
                hr = inputType.SetGUID(ref attrKey, ref majorType);
                if (hr < 0) { LastError = "Failed to set input video major type"; return false; }

                attrKey = MediaFoundation.MF_MT_SUBTYPE;
                var rgb32Guid = new Guid("00000016-0000-0010-8000-00AA00389B71");
                hr = inputType.SetGUID(ref attrKey, ref rgb32Guid);
                if (hr < 0) { LastError = "Failed to set RGB32 subtype"; return false; }

                attrKey = MediaFoundation.MF_MT_INTERLACE_MODE;
                hr = inputType.SetUINT32(ref attrKey, MediaFoundation.MFVideoInterlace_Progressive);
                if (hr < 0) { LastError = "Failed to set input interlace mode"; return false; }

                attrKey = MediaFoundation.MF_MT_FRAME_SIZE;
                var frameSizeValue = ((long)videoWidth << 32) | (long)videoHeight;
                hr = inputType.SetUINT64(ref attrKey, frameSizeValue);
                if (hr < 0) { LastError = "Failed to set input frame size"; return false; }

                attrKey = MediaFoundation.MF_MT_FRAME_RATE;
                var frameRateValue = ((long)FrameRateNumerator << 32) | (long)FrameRateDenominator;
                hr = inputType.SetUINT64(ref attrKey, frameRateValue);
                if (hr < 0) { LastError = "Failed to set input frame rate"; return false; }

                hr = sinkWriter!.SetInputMediaType(videoStreamIndex, inputType, IntPtr.Zero);
                if (hr < 0)
                {
                    LastError = $"SetInputMediaType for video failed with HRESULT 0x{hr:X8}";
                    logger.Error(LastError);
                    return false;
                }
            }
            finally
            {
                Marshal.ReleaseComObject(inputType);
            }

            logger.Debug($"Video stream configured: stream index {videoStreamIndex}");
            return true;
        }
        catch (Exception ex)
        {
            LastError = $"Failed to configure video stream: {ex.Message}";
            logger.Error(ex, LastError);
            return false;
        }
    }

    private bool ConfigureAudioStream()
    {
        try
        {
            var hr = MediaFoundation.MFCreateMediaType(out var outputType);
            if (hr < 0)
            {
                LastError = $"MFCreateMediaType for audio output failed with HRESULT 0x{hr:X8}";
                logger.Error(LastError);
                return false;
            }

            try
            {
                var attrKey = MediaFoundation.MF_MT_MAJOR_TYPE;
                var majorType = MediaFoundation.MFMediaType_Audio;
                hr = outputType.SetGUID(ref attrKey, ref majorType);
                if (hr < 0) { LastError = "Failed to set audio major type"; return false; }

                attrKey = MediaFoundation.MF_MT_SUBTYPE;
                var aacGuid = MediaFoundation.MFAudioFormat_AAC;
                hr = outputType.SetGUID(ref attrKey, ref aacGuid);
                if (hr < 0) { LastError = "Failed to set AAC subtype"; return false; }

                attrKey = MediaFoundation.MF_MT_AUDIO_NUM_CHANNELS;
                hr = outputType.SetUINT32(ref attrKey, AudioChannels);
                if (hr < 0) { LastError = "Failed to set audio channels"; return false; }

                attrKey = MediaFoundation.MF_MT_AUDIO_SAMPLES_PER_SECOND;
                hr = outputType.SetUINT32(ref attrKey, AudioSampleRate);
                if (hr < 0) { LastError = "Failed to set audio sample rate"; return false; }

                attrKey = MediaFoundation.MF_MT_AUDIO_BITS_PER_SAMPLE;
                hr = outputType.SetUINT32(ref attrKey, AudioBitsPerSample);
                if (hr < 0) { LastError = "Failed to set audio bits per sample"; return false; }

                attrKey = MediaFoundation.MF_MT_AVG_BITRATE;
                hr = outputType.SetUINT32(ref attrKey, AudioBitrate);
                if (hr < 0) { LastError = "Failed to set audio bitrate"; return false; }

                hr = sinkWriter!.AddStream(outputType, out audioStreamIndex);
                if (hr < 0)
                {
                    LastError = $"AddStream for audio failed with HRESULT 0x{hr:X8}";
                    logger.Error(LastError);
                    return false;
                }
            }
            finally
            {
                Marshal.ReleaseComObject(outputType);
            }

            hr = MediaFoundation.MFCreateMediaType(out var inputType);
            if (hr < 0)
            {
                LastError = $"MFCreateMediaType for audio input failed with HRESULT 0x{hr:X8}";
                logger.Error(LastError);
                return false;
            }

            try
            {
                var attrKey = MediaFoundation.MF_MT_MAJOR_TYPE;
                var majorType = MediaFoundation.MFMediaType_Audio;
                hr = inputType.SetGUID(ref attrKey, ref majorType);
                if (hr < 0) { LastError = "Failed to set input audio major type"; return false; }

                attrKey = MediaFoundation.MF_MT_SUBTYPE;
                var pcmGuid = new Guid("00000001-0000-0010-8000-00AA00389B71");
                hr = inputType.SetGUID(ref attrKey, ref pcmGuid);
                if (hr < 0) { LastError = "Failed to set PCM subtype"; return false; }

                attrKey = MediaFoundation.MF_MT_AUDIO_NUM_CHANNELS;
                hr = inputType.SetUINT32(ref attrKey, AudioChannels);
                if (hr < 0) { LastError = "Failed to set input audio channels"; return false; }

                attrKey = MediaFoundation.MF_MT_AUDIO_SAMPLES_PER_SECOND;
                hr = inputType.SetUINT32(ref attrKey, AudioSampleRate);
                if (hr < 0) { LastError = "Failed to set input audio sample rate"; return false; }

                attrKey = MediaFoundation.MF_MT_AUDIO_BITS_PER_SAMPLE;
                hr = inputType.SetUINT32(ref attrKey, AudioBitsPerSample);
                if (hr < 0) { LastError = "Failed to set input audio bits per sample"; return false; }

                attrKey = MediaFoundation.MF_MT_AUDIO_BLOCK_ALIGNMENT;
                var blockAlign = AudioChannels * (AudioBitsPerSample / 8);
                hr = inputType.SetUINT32(ref attrKey, blockAlign);
                if (hr < 0) { LastError = "Failed to set audio block alignment"; return false; }

                hr = sinkWriter!.SetInputMediaType(audioStreamIndex, inputType, IntPtr.Zero);
                if (hr < 0)
                {
                    LastError = $"SetInputMediaType for audio failed with HRESULT 0x{hr:X8}";
                    logger.Error(LastError);
                    return false;
                }
            }
            finally
            {
                Marshal.ReleaseComObject(inputType);
            }

            logger.Debug($"Audio stream configured: stream index {audioStreamIndex}");
            return true;
        }
        catch (Exception ex)
        {
            LastError = $"Failed to configure audio stream: {ex.Message}";
            logger.Error(ex, LastError);
            return false;
        }
    }

    public bool AddVideoFrame(Bitmap frame)
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(MediaFoundationEncoder));
        }

        if (!IsInitialized || sinkWriter == null)
        {
            LastError = "Encoder is not initialized";
            logger.Warn(LastError);
            return false;
        }

        if (frame == null)
        {
            LastError = "Frame cannot be null";
            logger.Warn(LastError);
            return false;
        }

        if (frame.Width != videoWidth || frame.Height != videoHeight)
        {
            LastError = $"Frame dimensions ({frame.Width}x{frame.Height}) don't match configured dimensions ({videoWidth}x{videoHeight})";
            logger.Warn(LastError);
            return false;
        }

        if (isFinalized)
        {
            LastError = "Cannot add frames after finalization";
            logger.Warn(LastError);
            return false;
        }

        try
        {
            var bmpData = frame.LockBits(
                new Rectangle(0, 0, frame.Width, frame.Height),
                ImageLockMode.ReadOnly,
                PixelFormat.Format32bppArgb);

            try
            {
                var frameSize = Math.Abs(bmpData.Stride) * bmpData.Height;

                var hr = MediaFoundation.MFCreateMemoryBuffer(frameSize, out var buffer);
                if (hr < 0)
                {
                    LastError = $"MFCreateMemoryBuffer failed with HRESULT 0x{hr:X8}";
                    logger.Error(LastError);
                    return false;
                }

                try
                {
                    hr = buffer.Lock(out var pBuffer, out _, out _);
                    if (hr < 0)
                    {
                        LastError = $"Buffer.Lock failed with HRESULT 0x{hr:X8}";
                        logger.Error(LastError);
                        return false;
                    }

                    try
                    {
                        var srcPtr = bmpData.Scan0;
                        var dstPtr = pBuffer;
                        var stride = bmpData.Stride;
                        var rowSize = videoWidth * 4;

                        for (var y = 0; y < videoHeight; y++)
                        {
                            var rowBuffer = new byte[rowSize];
                            Marshal.Copy(IntPtr.Add(srcPtr, y * stride), rowBuffer, 0, rowSize);
                            Marshal.Copy(rowBuffer, 0, IntPtr.Add(dstPtr, y * rowSize), rowSize);
                        }
                    }
                    finally
                    {
                        buffer.Unlock();
                    }

                    hr = buffer.SetCurrentLength(frameSize);
                    if (hr < 0)
                    {
                        LastError = $"SetCurrentLength failed with HRESULT 0x{hr:X8}";
                        logger.Error(LastError);
                        return false;
                    }

                    hr = MediaFoundation.MFCreateSample(out var sample);
                    if (hr < 0)
                    {
                        LastError = $"MFCreateSample failed with HRESULT 0x{hr:X8}";
                        logger.Error(LastError);
                        return false;
                    }

                    try
                    {
                        hr = sample.AddBuffer(buffer);
                        if (hr < 0)
                        {
                            LastError = $"AddBuffer failed with HRESULT 0x{hr:X8}";
                            logger.Error(LastError);
                            return false;
                        }

                        hr = sample.SetSampleTime(videoTimestamp);
                        if (hr < 0)
                        {
                            LastError = $"SetSampleTime failed with HRESULT 0x{hr:X8}";
                            logger.Error(LastError);
                            return false;
                        }

                        hr = sample.SetSampleDuration(FrameDuration100Ns);
                        if (hr < 0)
                        {
                            LastError = $"SetSampleDuration failed with HRESULT 0x{hr:X8}";
                            logger.Error(LastError);
                            return false;
                        }

                        hr = sinkWriter.WriteSample(videoStreamIndex, sample);
                        if (hr < 0)
                        {
                            LastError = $"WriteSample for video failed with HRESULT 0x{hr:X8}";
                            logger.Error(LastError);
                            return false;
                        }

                        videoTimestamp += FrameDuration100Ns;
                        return true;
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(sample);
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(buffer);
                }
            }
            finally
            {
                frame.UnlockBits(bmpData);
            }
        }
        catch (Exception ex)
        {
            LastError = $"Error adding video frame: {ex.Message}";
            logger.Error(ex, LastError);
            return false;
        }
    }

    public bool AddAudioFrame(byte[] audioData)
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(MediaFoundationEncoder));
        }

        if (!IsInitialized || sinkWriter == null)
        {
            LastError = "Encoder is not initialized";
            logger.Warn(LastError);
            return false;
        }

        if (audioData == null || audioData.Length == 0)
        {
            return true;
        }

        if (isFinalized)
        {
            LastError = "Cannot add audio after finalization";
            logger.Warn(LastError);
            return false;
        }

        try
        {
            var hr = MediaFoundation.MFCreateMemoryBuffer(audioData.Length, out var buffer);
            if (hr < 0)
            {
                LastError = $"MFCreateMemoryBuffer for audio failed with HRESULT 0x{hr:X8}";
                logger.Error(LastError);
                return false;
            }

            try
            {
                hr = buffer.Lock(out var pBuffer, out _, out _);
                if (hr < 0)
                {
                    LastError = $"Buffer.Lock for audio failed with HRESULT 0x{hr:X8}";
                    logger.Error(LastError);
                    return false;
                }

                try
                {
                    Marshal.Copy(audioData, 0, pBuffer, audioData.Length);
                }
                finally
                {
                    buffer.Unlock();
                }

                hr = buffer.SetCurrentLength(audioData.Length);
                if (hr < 0)
                {
                    LastError = $"SetCurrentLength for audio failed with HRESULT 0x{hr:X8}";
                    logger.Error(LastError);
                    return false;
                }

                hr = MediaFoundation.MFCreateSample(out var sample);
                if (hr < 0)
                {
                    LastError = $"MFCreateSample for audio failed with HRESULT 0x{hr:X8}";
                    logger.Error(LastError);
                    return false;
                }

                try
                {
                    hr = sample.AddBuffer(buffer);
                    if (hr < 0)
                    {
                        LastError = $"AddBuffer for audio failed with HRESULT 0x{hr:X8}";
                        logger.Error(LastError);
                        return false;
                    }

                    var audioDuration = (long)((double)audioData.Length / audioBytesPerSecond * 10_000_000);

                    hr = sample.SetSampleTime(audioTimestamp);
                    if (hr < 0)
                    {
                        LastError = $"SetSampleTime for audio failed with HRESULT 0x{hr:X8}";
                        logger.Error(LastError);
                        return false;
                    }

                    hr = sample.SetSampleDuration(audioDuration);
                    if (hr < 0)
                    {
                        LastError = $"SetSampleDuration for audio failed with HRESULT 0x{hr:X8}";
                        logger.Error(LastError);
                        return false;
                    }

                    hr = sinkWriter.WriteSample(audioStreamIndex, sample);
                    if (hr < 0)
                    {
                        LastError = $"WriteSample for audio failed with HRESULT 0x{hr:X8}";
                        logger.Error(LastError);
                        return false;
                    }

                    audioTimestamp += audioDuration;
                    return true;
                }
                finally
                {
                    Marshal.ReleaseComObject(sample);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(buffer);
            }
        }
        catch (Exception ex)
        {
            LastError = $"Error adding audio frame: {ex.Message}";
            logger.Error(ex, LastError);
            return false;
        }
    }

    public bool Finalize()
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(MediaFoundationEncoder));
        }

        if (!IsInitialized || sinkWriter == null)
        {
            LastError = "Encoder is not initialized";
            logger.Warn(LastError);
            return false;
        }

        if (isFinalized)
        {
            logger.Debug("Finalize called but already finalized");
            return true;
        }

        try
        {
            var hr = sinkWriter.Finalize();
            if (hr < 0)
            {
                LastError = $"Finalize failed with HRESULT 0x{hr:X8}";
                logger.Error(LastError);
                return false;
            }

            isFinalized = true;
            logger.Info($"MediaFoundationEncoder finalized: video duration {videoTimestamp / 10_000_000.0:F2}s");
            return true;
        }
        catch (Exception ex)
        {
            LastError = $"Error finalizing: {ex.Message}";
            logger.Error(ex, LastError);
            return false;
        }
    }

    private void Cleanup()
    {
        if (sinkWriter != null)
        {
            try
            {
                Marshal.ReleaseComObject(sinkWriter);
            }
            catch (Exception ex)
            {
                logger.Debug(ex, "Error releasing sink writer");
            }
            sinkWriter = null;
        }

        if (mfInitialized)
        {
            try
            {
                MediaFoundation.MFShutdown();
            }
            catch (Exception ex)
            {
                logger.Debug(ex, "Error shutting down Media Foundation");
            }
            mfInitialized = false;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        if (!isFinalized && IsInitialized)
        {
            try
            {
                Finalize();
            }
            catch (Exception ex)
            {
                logger.Debug(ex, "Error finalizing during dispose");
            }
        }

        Cleanup();
        disposed = true;
        logger.Debug("MediaFoundationEncoder disposed");
    }
}
