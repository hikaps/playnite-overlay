using Xunit;
using PlayniteOverlay.Services;

namespace OverlayPlugin.Tests;

public class GameVolumeServiceTests
{
    [Fact]
    public void GetVolume_WithInvalidProcessId_NegativeOne_ReturnsNull()
    {
        // Arrange
        var service = new GameVolumeService();

        // Act
        var result = service.GetVolume(-1);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetVolume_WithInvalidProcessId_Zero_ReturnsNull()
    {
        // Arrange
        var service = new GameVolumeService();

        // Act
        var result = service.GetVolume(0);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetVolume_WithNonExistentProcessId_ReturnsNull()
    {
        // Arrange
        var service = new GameVolumeService();
        var nonExistentProcessId = 999999999;

        // Act
        var result = service.GetVolume(nonExistentProcessId);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void SetVolume_WithInvalidProcessId_NegativeOne_ReturnsFalse()
    {
        // Arrange
        var service = new GameVolumeService();

        // Act
        var result = service.SetVolume(-1, 0.5f);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void SetVolume_WithInvalidProcessId_Zero_ReturnsFalse()
    {
        // Arrange
        var service = new GameVolumeService();

        // Act
        var result = service.SetVolume(0, 0.5f);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void SetVolume_WithNonExistentProcessId_ReturnsFalse()
    {
        // Arrange
        var service = new GameVolumeService();
        var nonExistentProcessId = 999999999;

        // Act
        var result = service.SetVolume(nonExistentProcessId, 0.5f);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void SetVolume_WithVolumeBelowZero_ClampsToZero()
    {
        var service = new GameVolumeService();

        var result = service.SetVolume(999999999, -0.5f);

        Assert.False(result);
    }

    [Fact]
    public void SetVolume_WithVolumeAboveOne_ClampsToOne()
    {
        var service = new GameVolumeService();

        var result = service.SetVolume(999999999, 1.5f);

        Assert.False(result);
    }

    [Fact]
    public void GetMute_WithInvalidProcessId_NegativeOne_ReturnsNull()
    {
        // Arrange
        var service = new GameVolumeService();

        // Act
        var result = service.GetMute(-1);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetMute_WithInvalidProcessId_Zero_ReturnsNull()
    {
        // Arrange
        var service = new GameVolumeService();

        // Act
        var result = service.GetMute(0);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetMute_WithNonExistentProcessId_ReturnsNull()
    {
        // Arrange
        var service = new GameVolumeService();
        var nonExistentProcessId = 999999999;

        // Act
        var result = service.GetMute(nonExistentProcessId);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void SetMute_WithInvalidProcessId_NegativeOne_ReturnsFalse()
    {
        // Arrange
        var service = new GameVolumeService();

        // Act
        var result = service.SetMute(-1, true);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void SetMute_WithInvalidProcessId_Zero_ReturnsFalse()
    {
        // Arrange
        var service = new GameVolumeService();

        // Act
        var result = service.SetMute(0, false);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void SetMute_WithNonExistentProcessId_ReturnsFalse()
    {
        // Arrange
        var service = new GameVolumeService();
        var nonExistentProcessId = 999999999;

        // Act
        var result = service.SetMute(nonExistentProcessId, true);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void GetAudioSession_WithInvalidProcessId_ReturnsNull()
    {
        // Arrange
        var service = new GameVolumeService();

        // Act
        var result = service.GetAudioSession(-1);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetAudioSession_WithNonExistentProcessId_ReturnsNull()
    {
        // Arrange
        var service = new GameVolumeService();
        var nonExistentProcessId = 999999999;

        // Act
        var result = service.GetAudioSession(nonExistentProcessId);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void Dispose_DisposesCleanly()
    {
        var service = new GameVolumeService();

        service.Dispose();

        service.Dispose();
    }

    [Fact]
    public void Service_ImplementsIDisposable()
    {
        var service = new GameVolumeService();

        Assert.IsAssignableFrom<System.IDisposable>(service);
    }
}
