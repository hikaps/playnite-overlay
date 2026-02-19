using MVVM = CommunityToolkit.Mvvm.ComponentModel;

namespace PlayniteOverlay.Models;

/// <summary>
/// Screenshot format options.
/// </summary>
public enum ScreenshotFormat
{
    /// <summary>
    /// PNG format (lossless compression).
    /// </summary>
    Png,
    
    /// <summary>
    /// JPEG format (lossy compression).
    /// </summary>
    Jpeg
}

/// <summary>
/// Video quality options.
/// </summary>
public enum VideoQuality
{
    /// <summary>
    /// Low quality setting.
    /// </summary>
    Low,
    
    /// <summary>
    /// Medium quality setting.
    /// </summary>
    Medium,
    
    /// <summary>
    /// High quality setting.
    /// </summary>
    High
}

/// <summary>
/// Settings for screen capture functionality.
/// </summary>
public class CaptureSettings : MVVM.ObservableObject
{
    private string outputPath = "%USERPROFILE%/Videos/Playnite Captures";
    /// <summary>
    /// Directory path where captured screenshots and videos are saved.
    /// Supports environment variables like %USERPROFILE%.
    /// </summary>
    public string OutputPath
    {
        get => outputPath;
        set => SetProperty(ref outputPath, value);
    }

    private ScreenshotFormat screenshotFormat = ScreenshotFormat.Png;
    /// <summary>
    /// Format for screenshot captures.
    /// </summary>
    public ScreenshotFormat ScreenshotFormat
    {
        get => screenshotFormat;
        set => SetProperty(ref screenshotFormat, value);
    }

    private VideoQuality videoQuality = VideoQuality.Medium;
    /// <summary>
    /// Quality setting for video captures.
    /// </summary>
    public VideoQuality VideoQuality
    {
        get => videoQuality;
        set => SetProperty(ref videoQuality, value);
    }
}
