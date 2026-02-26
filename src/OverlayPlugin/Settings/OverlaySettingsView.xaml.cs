using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PlayniteOverlay.Models;

namespace PlayniteOverlay;

public partial class OverlaySettingsView : UserControl
{
    public OverlaySettingsView()
    {
        InitializeComponent();
        Loaded += OverlaySettingsView_Loaded;
    }

    private void OverlaySettingsView_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateAddButtonState();
    }

    private OverlaySettings? Model => (DataContext as OverlaySettingsViewModel)?.Settings;

    private void UpdateAddButtonState()
    {
        if (Model != null && AddShortcutBtn != null)
        {
            AddShortcutBtn.IsEnabled = Model.Shortcuts.Count < OverlaySettings.MaxShortcuts;
        }
    }

    private static string BuildGestureFromKeyEvent(KeyEventArgs e)
    {
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

    private void Hotkey_Clear_Click(object sender, RoutedEventArgs e)
    {
        if (Model != null)
        {
            Model.CustomHotkey = string.Empty;
            Model.EnableCustomHotkey = false;
        }
    }


    private void AddShortcutBtn_Click(object sender, RoutedEventArgs e)
    {
        if (Model == null || Model.Shortcuts.Count >= OverlaySettings.MaxShortcuts)
        {
            return;
        }

        var newShortcut = new OverlayShortcut
        {
            Label = "New Shortcut",
            Command = string.Empty,
            Arguments = string.Empty,
            ActionType = ShortcutActionType.CommandLine,
            Hotkey = string.Empty
        };

        Model.Shortcuts.Add(newShortcut);
        UpdateAddButtonState();

        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (ShortcutsList.ItemContainerGenerator.ContainerFromItem(newShortcut) is ContentPresenter presenter)
            {
                var expander = FindVisualChild<Expander>(presenter);
                if (expander != null)
                {
                    expander.IsExpanded = true;
                }
            }
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void ShortcutDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is OverlayShortcut shortcut && Model != null)
        {
            Model.Shortcuts.Remove(shortcut);
            UpdateAddButtonState();
        }
    }

    private void ShortcutBrowse_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: OverlayShortcut shortcut })
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*",
                Title = "Select Command"
            };

            if (dialog.ShowDialog() == true)
            {
                shortcut.Command = dialog.FileName;
            }
        }
    }

    private void ShortcutExpander_Expanded(object sender, RoutedEventArgs e)
    {
        if (sender is Expander expander)
        {
            foreach (var item in ShortcutsList.Items)
            {
                if (ShortcutsList.ItemContainerGenerator.ContainerFromItem(item) is ContentPresenter presenter)
                {
                    var otherExpander = FindVisualChild<Expander>(presenter);
                    if (otherExpander != null && otherExpander != expander)
                    {
                        otherExpander.IsExpanded = false;
                    }
                }
            }
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T result)
            {
                return result;
            }
            var descendant = FindVisualChild<T>(child);
            if (descendant != null)
            {
                return descendant;
            }
        }
        return null;
    }

    private void ShortcutHotkey_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        if (sender is not TextBox box || box.Tag is not OverlayShortcut shortcut)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            shortcut.Hotkey = string.Empty;
            return;
        }

        var gesture = BuildGestureFromKeyEvent(e);
        if (!string.IsNullOrWhiteSpace(gesture))
        {
            shortcut.Hotkey = gesture;
        }
    }

    private void ShortcutHotkey_Clear_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: OverlayShortcut shortcut })
        {
            shortcut.Hotkey = string.Empty;
        }
    }
}
