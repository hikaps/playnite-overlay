using System.Windows.Controls;
using System.Windows.Input;
using WinForms = System.Windows.Forms;

namespace PlayniteOverlay;

public partial class OverlaySettingsView : UserControl
{
    public OverlaySettingsView()
    {
        InitializeComponent();
    }

    private OverlaySettings? Model => (DataContext as OverlaySettingsViewModel)?.Settings;

    private static string BuildGestureFromKeyEvent(KeyEventArgs e)
    {
        // Ignore modifier-only presses
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
        {
            return string.Empty;
        }

        var parts = new System.Collections.Generic.List<string>();
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) parts.Add("Ctrl");
        if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) parts.Add("Alt");
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) parts.Add("Shift");
        if ((Keyboard.Modifiers & ModifierKeys.Windows) != 0) parts.Add("Win");

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        parts.Add(key.ToString());
        return string.Join("+", parts);
    }

    private void Hotkey_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;

        if (e.Key == Key.Escape)
        {
            if (Model != null)
            {
                Model.CustomHotkey = string.Empty;
                Model.EnableCustomHotkey = false;
            }
            return;
        }

        var gesture = BuildGestureFromKeyEvent(e);
        if (!string.IsNullOrWhiteSpace(gesture) && Model != null)
        {
            Model.CustomHotkey = gesture;
            Model.EnableCustomHotkey = true;
        }
    }

    private void Hotkey_Clear_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (Model != null)
        {
            Model.CustomHotkey = string.Empty;
            Model.EnableCustomHotkey = false;
        }
    }

    private void BrowseOutputPath_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (Model?.Capture == null) return;

        using (var dialog = new WinForms.FolderBrowserDialog())
        {
            dialog.Description = "Select output folder for captures";
            dialog.ShowNewFolderButton = true;

            // Try to set initial directory to current path (expanded)
            try
            {
                var expandedPath = System.Environment.ExpandEnvironmentVariables(Model.Capture.OutputPath);
                if (System.IO.Directory.Exists(expandedPath))
                {
                    dialog.SelectedPath = expandedPath;
                }
            }
            catch
            {
                // Ignore if path is invalid
            }

            if (dialog.ShowDialog() == WinForms.DialogResult.OK)
            {
                Model.Capture.OutputPath = dialog.SelectedPath;
            }
        }
    }
}
