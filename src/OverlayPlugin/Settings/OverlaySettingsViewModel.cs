using System;
using System.Collections.Generic;
using Playnite.SDK;
using MVVM = CommunityToolkit.Mvvm.ComponentModel;

namespace PlayniteOverlay;

public class OverlaySettingsViewModel : MVVM.ObservableObject, ISettings
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

    public bool VerifySettings(out List<string> errors)
    {
        errors = new List<string>();

        if (Settings.EnableCustomHotkey && string.IsNullOrWhiteSpace(Settings.CustomHotkey))
        {
            errors.Add("Custom hotkey is enabled but not set.");
        }

        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Guide", "Start+Back", "Back+Start", "LB+RB", "RB+LB"
        };
        if (!allowed.Contains(Settings.ControllerCombo ?? string.Empty))
        {
            errors.Add("Controller combo must be Guide, Start+Back, or LB+RB.");
        }

        if (Settings.MaxRunningApps < 1 || Settings.MaxRunningApps > 50)
        {
            errors.Add("Maximum running apps must be between 1 and 50.");
        }

        if (Settings.ForceBorderlessMode && (Settings.BorderlessDelayMs < 1000 || Settings.BorderlessDelayMs > 30000))
        {
            errors.Add("Borderless delay must be between 1000 and 30000 ms.");
        }

        if (Settings.Capture != null)
        {
            var outputPath = Settings.Capture.OutputPath?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                errors.Add("Capture output path cannot be empty.");
            }
            else
            {
                try
                {
                    var expanded = System.Environment.ExpandEnvironmentVariables(outputPath);
                    if (string.IsNullOrWhiteSpace(expanded) || expanded.IndexOfAny(System.IO.Path.GetInvalidPathChars()) >= 0)
                    {
                        errors.Add("Capture output path contains invalid characters.");
                    }
                }
                catch
                {
                    errors.Add("Capture output path is invalid.");
                }
            }
        }

        return errors.Count == 0;
    }
}
