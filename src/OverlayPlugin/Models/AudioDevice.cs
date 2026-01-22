namespace PlayniteOverlay.Models;

/// <summary>
/// Represents an audio output device.
/// </summary>
public sealed class AudioDevice
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
}
