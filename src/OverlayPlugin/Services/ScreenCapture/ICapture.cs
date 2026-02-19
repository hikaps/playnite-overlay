using System;
using System.Drawing;

namespace PlayniteOverlay.Services;

/// <summary>
/// Interface for screen capture implementations.
/// Provides methods for capturing frames from a specific monitor.
/// </summary>
public interface ICapture : IDisposable
{
    /// <summary>
    /// Gets whether the capture method is supported on the current system.
    /// </summary>
    bool IsSupported { get; }

    /// <summary>
    /// Gets the last error message if a capture operation failed.
    /// </summary>
    string? LastError { get; }

    /// <summary>
    /// Initializes the capture mechanism for the specified monitor.
    /// </summary>
    /// <param name="monitorHandle">The monitor handle to capture from.</param>
    void Initialize(IntPtr monitorHandle);

    /// <summary>
    /// Captures a single frame from the monitor.
    /// </summary>
    /// <returns>A bitmap containing the captured frame, or null if capture failed.</returns>
    Bitmap? CaptureFrame();
}
