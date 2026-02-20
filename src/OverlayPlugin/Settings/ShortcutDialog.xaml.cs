using Microsoft.Win32;
using System.Windows;
using PlayniteOverlay.Models;

namespace PlayniteOverlay;

public partial class ShortcutDialog : Window
{
    public OverlayShortcut? Shortcut { get; private set; }

    public ShortcutDialog()
    {
        InitializeComponent();
    }

    public ShortcutDialog(OverlayShortcut shortcut) : this()
    {
        LabelTextBox.Text = shortcut.Label;
        CommandTextBox.Text = shortcut.Command;
        ArgumentsTextBox.Text = shortcut.Arguments;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*",
            Title = "Select executable"
        };

        if (dialog.ShowDialog() == true)
        {
            CommandTextBox.Text = dialog.FileName;
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var label = LabelTextBox.Text?.Trim() ?? string.Empty;
        var command = CommandTextBox.Text?.Trim() ?? string.Empty;
        var arguments = ArgumentsTextBox.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(label))
        {
            MessageBox.Show("Label is required.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            LabelTextBox.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(command))
        {
            MessageBox.Show("Command is required.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            CommandTextBox.Focus();
            return;
        }

        Shortcut = new OverlayShortcut
        {
            Label = label,
            Command = command,
            Arguments = arguments
        };

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
