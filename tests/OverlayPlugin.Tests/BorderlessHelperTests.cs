using Xunit;
using PlayniteOverlay;
using System;

namespace OverlayPlugin.Tests;

public class BorderlessHelperTests
{
    [Fact]
    public void WindowState_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var state = new BorderlessHelper.WindowState();

        // Assert
        Assert.Equal(IntPtr.Zero, state.Handle);
        Assert.Equal(0u, state.OriginalStyle);
        Assert.Equal(0u, state.OriginalExStyle);
        Assert.Equal(0, state.OriginalX);
        Assert.Equal(0, state.OriginalY);
        Assert.Equal(0, state.OriginalWidth);
        Assert.Equal(0, state.OriginalHeight);
    }

    [Fact]
    public void WindowState_Properties_CanBeSetAndRetrieved()
    {
        // Arrange
        var handle = new IntPtr(12345);
        var state = new BorderlessHelper.WindowState
        {
            Handle = handle,
            OriginalStyle = 0x00C00000,
            OriginalExStyle = 0x00000100,
            OriginalX = 100,
            OriginalY = 200,
            OriginalWidth = 1920,
            OriginalHeight = 1080
        };

        // Assert
        Assert.Equal(handle, state.Handle);
        Assert.Equal(0x00C00000u, state.OriginalStyle);
        Assert.Equal(0x00000100u, state.OriginalExStyle);
        Assert.Equal(100, state.OriginalX);
        Assert.Equal(200, state.OriginalY);
        Assert.Equal(1920, state.OriginalWidth);
        Assert.Equal(1080, state.OriginalHeight);
    }

    [Fact]
    public void RestoreWindow_WithNull_ReturnsFalse()
    {
        // Act
        var result = BorderlessHelper.RestoreWindow(null);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void RestoreWindow_WithZeroHandle_ReturnsFalse()
    {
        // Arrange
        var state = new BorderlessHelper.WindowState
        {
            Handle = IntPtr.Zero,
            OriginalStyle = 0x00C00000,
            OriginalExStyle = 0x00000100
        };

        // Act
        var result = BorderlessHelper.RestoreWindow(state);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void GetMainWindowHandle_WithInvalidProcessId_ReturnsZero()
    {
        // Arrange - use an impossible process ID
        var invalidProcessId = -1;

        // Act
        var result = BorderlessHelper.GetMainWindowHandle(invalidProcessId);

        // Assert
        Assert.Equal(IntPtr.Zero, result);
    }

    [Fact]
    public void GetMainWindowHandle_WithNonExistentProcessId_ReturnsZero()
    {
        // Arrange - use a very high process ID that likely doesn't exist
        var nonExistentProcessId = 999999999;

        // Act
        var result = BorderlessHelper.GetMainWindowHandle(nonExistentProcessId);

        // Assert
        Assert.Equal(IntPtr.Zero, result);
    }

    [Fact]
    public void HasWindowBorders_WithZeroHandle_ReturnsFalse()
    {
        // Act
        var result = BorderlessHelper.HasWindowBorders(IntPtr.Zero);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsLikelyExclusiveFullscreen_WithZeroHandle_ReturnsFalse()
    {
        // Act
        var result = BorderlessHelper.IsLikelyExclusiveFullscreen(IntPtr.Zero);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void MakeBorderless_WithZeroHandle_ReturnsNull()
    {
        // Act
        var result = BorderlessHelper.MakeBorderless(IntPtr.Zero);

        // Assert
        Assert.Null(result);
    }
}
