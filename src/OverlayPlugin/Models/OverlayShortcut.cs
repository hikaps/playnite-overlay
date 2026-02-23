using System.ComponentModel;

namespace PlayniteOverlay.Models;

/// <summary>
/// Represents a custom shortcut command.
/// </summary>
public sealed class OverlayShortcut : INotifyPropertyChanged
{
    private string label = string.Empty;
    private string command = string.Empty;
    private string arguments = string.Empty;

    public string Label
    {
        get => label;
        set
        {
            if (label != value)
            {
                label = value;
                OnPropertyChanged(nameof(Label));
            }
        }
    }

    public string Command
    {
        get => command;
        set
        {
            if (command != value)
            {
                command = value;
                OnPropertyChanged(nameof(Command));
            }
        }
    }

    public string Arguments
    {
        get => arguments;
        set
        {
            if (arguments != value)
            {
                arguments = value;
                OnPropertyChanged(nameof(Arguments));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
