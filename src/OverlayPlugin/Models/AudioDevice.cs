using System.ComponentModel;

namespace PlayniteOverlay.Models;

/// <summary>
/// Represents an audio output device.
/// </summary>
public sealed class AudioDevice : INotifyPropertyChanged
{
    private bool isDefault;
    
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    
    public bool IsDefault
    {
        get => isDefault;
        set
        {
            if (isDefault != value)
            {
                isDefault = value;
                OnPropertyChanged(nameof(IsDefault));
            }
        }
    }
    
    public event PropertyChangedEventHandler? PropertyChanged;
    
    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
