using CommunityToolkit.Mvvm.ComponentModel;
using Playnite.SDK;

namespace PlayniteOverlay;

public class OverlaySettingsViewModel : ObservableObject, ISettings
{
    private readonly OverlayPlugin plugin;

    public OverlaySettings Settings { get; private set; }

    public OverlaySettingsViewModel(OverlayPlugin plugin)
    {
        this.plugin = plugin;
        Settings = plugin.LoadPluginSettings<OverlaySettings>() ?? new OverlaySettings();
    }

    public void BeginEdit()
    {
        // No special handling required.
    }

    public void CancelEdit()
    {
        // Reload previous settings from disk
        var saved = plugin.LoadPluginSettings<OverlaySettings>();
        if (saved != null)
        {
            Settings = saved;
            OnPropertyChanged(nameof(Settings));
        }
    }

    public void EndEdit()
    {
        plugin.SavePluginSettings(Settings);
        plugin.ApplySettings(Settings);
    }
}
