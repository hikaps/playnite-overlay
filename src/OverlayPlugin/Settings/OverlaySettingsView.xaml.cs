using System.Windows.Controls;
using System.Windows.Input;

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

    private void AddShortcutBtn_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (Model != null && Model.Shortcuts.Count >= OverlaySettings.MaxShortcuts)
        {
            System.Windows.MessageBox.Show($"Maximum {OverlaySettings.MaxShortcuts} shortcuts allowed.", "Limit Reached", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }

        var dialog = new ShortcutDialog();
        dialog.Owner = System.Windows.Application.Current.MainWindow;
        if (dialog.ShowDialog() == true && dialog.Shortcut != null && Model != null)
        {
            Model.Shortcuts.Add(dialog.Shortcut);
        }
    }

    private void EditShortcutBtn_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var selected = ShortcutsList.SelectedItem as Models.OverlayShortcut;
        if (selected == null || Model == null)
        {
            return;
        }

        var dialog = new ShortcutDialog(selected);
        dialog.Owner = System.Windows.Application.Current.MainWindow;
        if (dialog.ShowDialog() == true && dialog.Shortcut != null)
        {
            var index = Model.Shortcuts.IndexOf(selected);
            if (index >= 0)
            {
                Model.Shortcuts[index] = dialog.Shortcut;
            }
        }
    }

    private void DeleteShortcutBtn_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var selected = ShortcutsList.SelectedItem as Models.OverlayShortcut;
        if (selected != null && Model != null)
        {
            Model.Shortcuts.Remove(selected);
        }
    }
}
