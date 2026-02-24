using System;
using System.Threading.Tasks;

namespace PlayniteOverlay;

public interface ICaptureService
{
    string Name { get; }
    bool IsAvailable();
    Task TakeScreenshotAsync();
    Task StartRecordingAsync();
    Task StopRecordingAsync();
    Task ToggleRecordingAsync();
    bool IsRecording { get; }
    event EventHandler<bool>? RecordingStateChanged;
}
