using System;

namespace PlayniteOverlay.Models;

public sealed class RunningApp
{
    public string Title { get; set; } = string.Empty;
    public string? ImagePath { get; set; }
    public IntPtr WindowHandle { get; set; }
    public Guid? GameId { get; set; }
    public int ProcessId { get; set; }
    public AppType Type { get; set; }
    public Action? OnSwitch { get; set; }
}

public enum AppType
{
    /// <summary>
    /// Game tracked by Playnite with full metadata (cover, playtime, etc.)
    /// </summary>
    PlayniteGame,

    /// <summary>
    /// Process that looks like a game but not in Playnite database
    /// </summary>
    DetectedGame,

    /// <summary>
    /// Generic application (browser, editor, etc.)
    /// </summary>
    GenericApp
}
