using System.Collections.ObjectModel;
using MVVM = CommunityToolkit.Mvvm.ComponentModel;

namespace PlayniteOverlay;

public class OverlaySettings : MVVM.ObservableObject
{
    public OverlaySettings()
    {
        shortcuts = new ObservableCollection<Models.OverlayShortcut>();
    }

    private bool useControllerToOpen = true;
    public bool UseControllerToOpen
    {
        get => useControllerToOpen;
        set => SetProperty(ref useControllerToOpen, value);
    }

    private string controllerCombo = "Guide"; // Placeholder; later map to explicit buttons
    public string ControllerCombo
    {
        get => controllerCombo;
        set => SetProperty(ref controllerCombo, value);
    }

    private bool enableCustomHotkey = true;
    public bool EnableCustomHotkey
    {
        get => enableCustomHotkey;
        set => SetProperty(ref enableCustomHotkey, value);
    }

    private string customHotkey = "Ctrl+Alt+O";
    public string CustomHotkey
    {
        get => customHotkey;
        set => SetProperty(ref customHotkey, value);
    }

    private bool controllerAlwaysActive = true;
    public bool ControllerAlwaysActive
    {
        get => controllerAlwaysActive;
        set => SetProperty(ref controllerAlwaysActive, value);
    }

    private bool pcGamesOnly = false;
    /// <summary>
    /// When enabled, controller input is only active for PC platform games.
    /// Emulated games will not trigger controller overlay activation.
    /// </summary>
    public bool PcGamesOnly
    {
        get => pcGamesOnly;
        set => SetProperty(ref pcGamesOnly, value);
    }

    private bool showGenericApps = false;
    public bool ShowGenericApps
    {
        get => showGenericApps;
        set => SetProperty(ref showGenericApps, value);
    }

    private int maxRunningApps = 4;
    public int MaxRunningApps
    {
        get => maxRunningApps;
        set => SetProperty(ref maxRunningApps, value);
    }

    private bool forceBorderlessMode = false;
    /// <summary>
    /// When enabled, automatically converts windowed games to borderless fullscreen mode.
    /// This helps the overlay appear over games that don't natively support borderless.
    /// </summary>
    public bool ForceBorderlessMode
    {
        get => forceBorderlessMode;
        set => SetProperty(ref forceBorderlessMode, value);
    }

    private int borderlessDelayMs = 3000;
    /// <summary>
    /// Delay in milliseconds after game starts before applying borderless mode.
    /// Allows the game window to fully initialize.
    /// </summary>
    public int BorderlessDelayMs
    {
        get => borderlessDelayMs;
        set => SetProperty(ref borderlessDelayMs, value);
    }

    private bool showNotifications = true;
    /// <summary>
    /// When enabled, shows notifications for app switching, exit operations, and errors.
    /// </summary>
    public bool ShowNotifications
    {
        get => showNotifications;
        set => SetProperty(ref showNotifications, value);
    }

    private bool showAchievements = true;
    /// <summary>
    /// When enabled, shows achievement progress in the NOW PLAYING section if SuccessStory plugin is installed.
    /// </summary>
    public bool ShowAchievements
    {
        get => showAchievements;
        set => SetProperty(ref showAchievements, value);
    }

    private int maxRecentAchievements = 3;
    /// <summary>
    /// Maximum number of recently unlocked achievements to display.
    /// </summary>
    public int MaxRecentAchievements
    {
        get => maxRecentAchievements;
        set => SetProperty(ref maxRecentAchievements, value);
    }

    private int maxLockedAchievements = 3;
    /// <summary>
    /// Maximum number of locked achievements to display.
    /// </summary>
    public int MaxLockedAchievements
    {
        get => maxLockedAchievements;
        set => SetProperty(ref maxLockedAchievements, value);
    }

    private ObservableCollection<Models.OverlayShortcut> shortcuts;
    /// <summary>
    /// User-defined keyboard shortcuts that can be triggered from the overlay.
    /// </summary>
    public ObservableCollection<Models.OverlayShortcut> Shortcuts
    {
        get => shortcuts;
        set => SetProperty(ref shortcuts, value);
    }

    /// <summary>
    /// Maximum number of shortcuts allowed.
    /// </summary>
    public const int MaxShortcuts = 10;
}
