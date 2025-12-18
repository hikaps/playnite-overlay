using Xunit;
using PlayniteOverlay;
using System.ComponentModel;

namespace OverlayPlugin.Tests;

public class OverlaySettingsTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var settings = new OverlaySettings();

        // Assert
        Assert.True(settings.UseControllerToOpen);
        Assert.Equal("Guide", settings.ControllerCombo);
        Assert.True(settings.EnableCustomHotkey);
        Assert.Equal("Ctrl+Alt+O", settings.CustomHotkey);
        Assert.True(settings.ControllerAlwaysActive);
        Assert.True(settings.ShowGenericApps);
        Assert.Equal(4, settings.MaxRunningApps);
        Assert.False(settings.ForceBorderlessMode);
        Assert.Equal(3000, settings.BorderlessDelayMs);
    }

    [Fact]
    public void ForceBorderlessMode_Default_IsFalse()
    {
        // Arrange & Act
        var settings = new OverlaySettings();

        // Assert
        Assert.False(settings.ForceBorderlessMode);
    }

    [Fact]
    public void ForceBorderlessMode_CanBeSet()
    {
        // Arrange
        var settings = new OverlaySettings();

        // Act
        settings.ForceBorderlessMode = true;

        // Assert
        Assert.True(settings.ForceBorderlessMode);
    }

    [Fact]
    public void BorderlessDelayMs_Default_Is3000()
    {
        // Arrange & Act
        var settings = new OverlaySettings();

        // Assert
        Assert.Equal(3000, settings.BorderlessDelayMs);
    }

    [Fact]
    public void BorderlessDelayMs_CanBeSet()
    {
        // Arrange
        var settings = new OverlaySettings();

        // Act
        settings.BorderlessDelayMs = 5000;

        // Assert
        Assert.Equal(5000, settings.BorderlessDelayMs);
    }

    [Fact]
    public void UseControllerToOpen_CanBeToggled()
    {
        // Arrange
        var settings = new OverlaySettings();

        // Act
        settings.UseControllerToOpen = false;

        // Assert
        Assert.False(settings.UseControllerToOpen);
    }

    [Fact]
    public void ControllerCombo_CanBeChanged()
    {
        // Arrange
        var settings = new OverlaySettings();

        // Act
        settings.ControllerCombo = "LB+RB";

        // Assert
        Assert.Equal("LB+RB", settings.ControllerCombo);
    }

    [Fact]
    public void CustomHotkey_CanBeChanged()
    {
        // Arrange
        var settings = new OverlaySettings();

        // Act
        settings.CustomHotkey = "Ctrl+Shift+G";

        // Assert
        Assert.Equal("Ctrl+Shift+G", settings.CustomHotkey);
    }

    [Fact]
    public void MaxRunningApps_CanBeChanged()
    {
        // Arrange
        var settings = new OverlaySettings();

        // Act
        settings.MaxRunningApps = 8;

        // Assert
        Assert.Equal(8, settings.MaxRunningApps);
    }

    [Fact]
    public void PropertyChanged_FiresForForceBorderlessMode()
    {
        // Arrange
        var settings = new OverlaySettings();
        string? changedProperty = null;
        settings.PropertyChanged += (_, e) => changedProperty = e.PropertyName;

        // Act
        settings.ForceBorderlessMode = true;

        // Assert
        Assert.Equal(nameof(OverlaySettings.ForceBorderlessMode), changedProperty);
    }

    [Fact]
    public void PropertyChanged_FiresForBorderlessDelayMs()
    {
        // Arrange
        var settings = new OverlaySettings();
        string? changedProperty = null;
        settings.PropertyChanged += (_, e) => changedProperty = e.PropertyName;

        // Act
        settings.BorderlessDelayMs = 5000;

        // Assert
        Assert.Equal(nameof(OverlaySettings.BorderlessDelayMs), changedProperty);
    }

    [Fact]
    public void PropertyChanged_DoesNotFireWhenValueUnchanged()
    {
        // Arrange
        var settings = new OverlaySettings();
        var eventFired = false;
        settings.PropertyChanged += (_, _) => eventFired = true;

        // Act - set to same default value
        settings.ForceBorderlessMode = false;

        // Assert
        Assert.False(eventFired);
    }

    [Fact]
    public void Settings_ImplementsINotifyPropertyChanged()
    {
        // Arrange & Act
        var settings = new OverlaySettings();

        // Assert
        Assert.IsAssignableFrom<INotifyPropertyChanged>(settings);
    }
}
