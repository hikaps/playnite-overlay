using MVVM = CommunityToolkit.Mvvm.ComponentModel;

namespace PlayniteOverlay;

public class OverlaySettings : MVVM.ObservableObject
{
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

    private bool showGenericApps = true;
    public bool ShowGenericApps
    {
        get => showGenericApps;
        set => SetProperty(ref showGenericApps, value);
    }

    private int maxRunningApps = 10;
    public int MaxRunningApps
    {
        get => maxRunningApps;
        set => SetProperty(ref maxRunningApps, value);
    }
}
