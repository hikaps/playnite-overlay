using System;
using System.Threading.Tasks;
using Moq;
using PlayniteOverlay;
using PlayniteOverlay.Services;
using Xunit;

namespace OverlayPlugin.Tests;

public class CaptureManagerTests
{
    private readonly OverlaySettings defaultSettings = new OverlaySettings
    {
        EnableCapture = true,
        ScreenshotHotkey = "PrintScreen",
        RecordHotkey = "F9",
        ObsWebSocketPassword = ""
    };

    [Fact]
    public async Task ShareXAvailable_ScreenshotUsesShareX()
    {
        // Arrange
        var mockShareX = new Mock<ICaptureService>();
        mockShareX.Setup(s => s.Name).Returns("ShareX");
        mockShareX.Setup(s => s.IsAvailable()).Returns(true);

        var mockObs = new Mock<ICaptureService>();
        mockObs.Setup(s => s.Name).Returns("OBS WebSocket");
        mockObs.Setup(s => s.IsAvailable()).Returns(false);

        var mockSendInput = new Mock<ICaptureService>();
        mockSendInput.Setup(s => s.Name).Returns("SendInput Hotkey");
        mockSendInput.Setup(s => s.IsAvailable()).Returns(true);

        var manager = new CaptureManager(
            defaultSettings,
            mockShareX.Object,
            mockObs.Object,
            mockSendInput.Object);

        // Act
        await manager.DetectBackendsAsync();

        // Assert
        Assert.True(manager.CanScreenshot);
        Assert.Equal("ShareX", manager.ScreenshotServiceName);
        mockShareX.Verify(s => s.IsAvailable(), Times.Once);
    }

    [Fact]
    public async Task ShareXUnavailable_ScreenshotFallsBackToSendInput()
    {
        // Arrange
        var mockShareX = new Mock<ICaptureService>();
        mockShareX.Setup(s => s.Name).Returns("ShareX");
        mockShareX.Setup(s => s.IsAvailable()).Returns(false);

        var mockObs = new Mock<ICaptureService>();
        mockObs.Setup(s => s.Name).Returns("OBS WebSocket");
        mockObs.Setup(s => s.IsAvailable()).Returns(false);

        var mockSendInput = new Mock<ICaptureService>();
        mockSendInput.Setup(s => s.Name).Returns("SendInput Hotkey");
        mockSendInput.Setup(s => s.IsAvailable()).Returns(true);

        var manager = new CaptureManager(
            defaultSettings,
            mockShareX.Object,
            mockObs.Object,
            mockSendInput.Object);

        // Act
        await manager.DetectBackendsAsync();

        // Assert
        Assert.True(manager.CanScreenshot);
        Assert.Equal("SendInput Hotkey", manager.ScreenshotServiceName);
    }

    [Fact]
    public async Task ObsAvailable_RecordingUsesObs()
    {
        // Arrange
        var mockShareX = new Mock<ICaptureService>();
        mockShareX.Setup(s => s.Name).Returns("ShareX");
        mockShareX.Setup(s => s.IsAvailable()).Returns(false);

        var mockObs = new Mock<ICaptureService>();
        mockObs.Setup(s => s.Name).Returns("OBS WebSocket");
        mockObs.Setup(s => s.IsAvailable()).Returns(true);

        var mockSendInput = new Mock<ICaptureService>();
        mockSendInput.Setup(s => s.Name).Returns("SendInput Hotkey");
        mockSendInput.Setup(s => s.IsAvailable()).Returns(true);

        var manager = new CaptureManager(
            defaultSettings,
            mockShareX.Object,
            mockObs.Object,
            mockSendInput.Object);

        // Act
        await manager.DetectBackendsAsync();

        // Assert
        Assert.True(manager.CanRecord);
        Assert.Equal("OBS WebSocket", manager.RecordingServiceName);
        mockObs.Verify(s => s.IsAvailable(), Times.Once);
    }

    [Fact]
    public async Task ObsUnavailable_RecordingFallsBackToSendInput()
    {
        // Arrange
        var mockShareX = new Mock<ICaptureService>();
        mockShareX.Setup(s => s.Name).Returns("ShareX");
        mockShareX.Setup(s => s.IsAvailable()).Returns(false);

        var mockObs = new Mock<ICaptureService>();
        mockObs.Setup(s => s.Name).Returns("OBS WebSocket");
        mockObs.Setup(s => s.IsAvailable()).Returns(false);

        var mockSendInput = new Mock<ICaptureService>();
        mockSendInput.Setup(s => s.Name).Returns("SendInput Hotkey");
        mockSendInput.Setup(s => s.IsAvailable()).Returns(true);

        var manager = new CaptureManager(
            defaultSettings,
            mockShareX.Object,
            mockObs.Object,
            mockSendInput.Object);

        // Act
        await manager.DetectBackendsAsync();

        // Assert
        Assert.True(manager.CanRecord);
        Assert.Equal("SendInput Hotkey", manager.RecordingServiceName);
    }

    [Fact]
    public async Task NoBackendsAvailable_CanScreenshotAndCanRecordAreFalse()
    {
        // Arrange
        var mockShareX = new Mock<ICaptureService>();
        mockShareX.Setup(s => s.IsAvailable()).Returns(false);

        var mockObs = new Mock<ICaptureService>();
        mockObs.Setup(s => s.IsAvailable()).Returns(false);

        var manager = new CaptureManager(
            defaultSettings,
            mockShareX.Object,
            mockObs.Object,
            null);

        // Act
        await manager.DetectBackendsAsync();

        // Assert
        Assert.False(manager.CanScreenshot);
        Assert.False(manager.CanRecord);
    }

    [Fact]
    public async Task RecordingStateChangedEvent_ForwardedCorrectly()
    {
        // Arrange
        var mockObs = new Mock<ICaptureService>();
        mockObs.Setup(s => s.Name).Returns("OBS WebSocket");
        mockObs.Setup(s => s.IsAvailable()).Returns(true);

        var manager = new CaptureManager(
            defaultSettings,
            null,
            mockObs.Object,
            null);

        await manager.DetectBackendsAsync();

        bool? receivedState = null;
        manager.RecordingStateChanged += (sender, isRecording) => receivedState = isRecording;

        // Act - Raise event from mock service
        mockObs.Raise(s => s.RecordingStateChanged += null, true);

        // Assert
        Assert.True(receivedState);

        // Act - Raise again with false
        mockObs.Raise(s => s.RecordingStateChanged += null, false);

        // Assert
        Assert.False(receivedState);
    }

    [Fact]
    public async Task CaptureDisabled_CanScreenshotAndCanRecordAreFalse()
    {
        // Arrange
        var settings = new OverlaySettings
        {
            EnableCapture = false,
            ScreenshotHotkey = "PrintScreen",
            RecordHotkey = "F9"
        };

        var mockShareX = new Mock<ICaptureService>();
        mockShareX.Setup(s => s.IsAvailable()).Returns(true);

        var mockObs = new Mock<ICaptureService>();
        mockObs.Setup(s => s.IsAvailable()).Returns(true);

        var mockSendInput = new Mock<ICaptureService>();
        mockSendInput.Setup(s => s.IsAvailable()).Returns(true);

        var manager = new CaptureManager(
            settings,
            mockShareX.Object,
            mockObs.Object,
            mockSendInput.Object);

        // Act
        await manager.DetectBackendsAsync();

        // Assert
        Assert.False(manager.CanScreenshot);
        Assert.False(manager.CanRecord);
        mockShareX.Verify(s => s.IsAvailable(), Times.Never);
        mockObs.Verify(s => s.IsAvailable(), Times.Never);
    }

    [Fact]
    public async Task TakeScreenshotAsync_DelegatesToActiveService()
    {
        // Arrange
        var mockSendInput = new Mock<ICaptureService>();
        mockSendInput.Setup(s => s.Name).Returns("SendInput Hotkey");
        mockSendInput.Setup(s => s.IsAvailable()).Returns(true);
        mockSendInput.Setup(s => s.TakeScreenshotAsync()).Returns(Task.CompletedTask);

        var manager = new CaptureManager(
            defaultSettings,
            null,
            null,
            mockSendInput.Object);

        await manager.DetectBackendsAsync();

        // Act
        await manager.TakeScreenshotAsync();

        // Assert
        mockSendInput.Verify(s => s.TakeScreenshotAsync(), Times.Once);
    }

    [Fact]
    public async Task ToggleRecordingAsync_DelegatesToActiveService()
    {
        // Arrange
        var mockSendInput = new Mock<ICaptureService>();
        mockSendInput.Setup(s => s.Name).Returns("SendInput Hotkey");
        mockSendInput.Setup(s => s.IsAvailable()).Returns(true);
        mockSendInput.Setup(s => s.ToggleRecordingAsync()).Returns(Task.CompletedTask);

        var manager = new CaptureManager(
            defaultSettings,
            null,
            null,
            mockSendInput.Object);

        await manager.DetectBackendsAsync();

        // Act
        await manager.ToggleRecordingAsync();

        // Assert
        mockSendInput.Verify(s => s.ToggleRecordingAsync(), Times.Once);
    }

    [Fact]
    public async Task IsRecording_ReturnsServiceValue()
    {
        // Arrange
        var mockObs = new Mock<ICaptureService>();
        mockObs.Setup(s => s.Name).Returns("OBS WebSocket");
        mockObs.Setup(s => s.IsAvailable()).Returns(true);
        mockObs.Setup(s => s.IsRecording).Returns(true);

        var manager = new CaptureManager(
            defaultSettings,
            null,
            mockObs.Object,
            null);

        await manager.DetectBackendsAsync();

        // Assert
        Assert.True(manager.IsRecording);
    }

    [Fact]
    public async Task IsRecording_NoService_ReturnsFalse()
    {
        // Arrange
        var manager = new CaptureManager(
            defaultSettings,
            null,
            null,
            null);

        await manager.DetectBackendsAsync();

        // Assert
        Assert.False(manager.IsRecording);
    }

    [Fact]
    public async Task Dispose_UnsubscribesFromEvents()
    {
        // Arrange
        var mockObs = new Mock<ICaptureService>();
        mockObs.Setup(s => s.Name).Returns("OBS WebSocket");
        mockObs.Setup(s => s.IsAvailable()).Returns(true);

        var manager = new CaptureManager(
            defaultSettings,
            null,
            mockObs.Object,
            null);

        await manager.DetectBackendsAsync();

        // Act
        manager.Dispose();

        // Assert
        bool eventReceived = false;
        manager.RecordingStateChanged += (sender, isRecording) => eventReceived = true;
        mockObs.Raise(s => s.RecordingStateChanged += null, true);
        Assert.False(eventReceived);
    }

    [Fact]
    public void Constructor_WithNullSettings_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new CaptureManager(null!));
    }
}
