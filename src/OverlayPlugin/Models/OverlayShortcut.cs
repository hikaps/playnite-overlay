using System.ComponentModel;

namespace PlayniteOverlay.Models;

/// <summary>
/// Represents a custom shortcut command.
/// </summary>
public sealed class OverlayShortcut : INotifyPropertyChanged
{
    private ShortcutActionType actionType = ShortcutActionType.CommandLine;
    private string label = string.Empty;
    private string command = string.Empty;
    private string arguments = string.Empty;
    private string hotkey = string.Empty;

    public ShortcutActionType ActionType
    {
        get => actionType;
        set
        {
            if (actionType != value)
            {
                actionType = value;
                OnPropertyChanged(nameof(ActionType));
            }
        }
    }

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

    public string Hotkey
    {
        get => hotkey;
        set
        {
            if (hotkey != value)
            {
                hotkey = value;
                OnPropertyChanged(nameof(Hotkey));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// Defines the type of action a shortcut can perform.
/// </summary>
public enum ShortcutActionType
{
    /// <summary>
    /// Execute a shell command or launch an executable.
    /// </summary>
    CommandLine,
    
    /// <summary>
    /// Simulate keyboard input (hotkey) using SendInput API.
    /// </summary>
    SendInput
}
