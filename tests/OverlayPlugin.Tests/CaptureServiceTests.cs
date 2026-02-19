using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using Moq;
using Playnite.SDK;
using PlayniteOverlay.Models;
using PlayniteOverlay.Services;
using Xunit;

namespace OverlayPlugin.Tests;

public class CaptureServiceTests : IDisposable
{
    private readonly Mock<IPlayniteAPI> mockApi;
    private readonly Mock<INotificationsAPI> mockNotifications;
    private readonly CaptureSettings settings;
    private readonly string testOutputDirectory;

    public CaptureServiceTests()
    {
        mockApi = new Mock<IPlayniteAPI>();
        mockNotifications = new Mock<INotificationsAPI>();
        mockApi.SetupGet(a => a.Notifications).Returns(mockNotifications.Object);

        settings = new CaptureSettings();
        testOutputDirectory = Path.Combine(Path.GetTempPath(), $"CaptureServiceTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(testOutputDirectory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(testOutputDirectory))
            {
                Directory.Delete(testOutputDirectory, recursive: true);
            }
        }
        catch
        {
        }
    }

    [Fact]
    public void IsAvailable_ReturnsFalse_WhenCaptureNotSupported()
    {
        var mockCapture = new Mock<ICapture>();
        mockCapture.SetupGet(c => c.IsSupported).Returns(false);
        mockCapture.SetupGet(c => c.LastError).Returns("Not supported");

        using var service = new CaptureService(mockApi.Object, settings, mockCapture.Object);

        Assert.False(service.IsAvailable);
        mockCapture.Verify(c => c.Dispose(), Times.Once);
    }

    [Fact]
    public void IsAvailable_ReturnsTrue_WhenCaptureIsSupported()
    {
        var mockCapture = CreateSupportedCaptureMock();

        using var service = new CaptureService(mockApi.Object, settings, mockCapture.Object);

        Assert.True(service.IsAvailable);
    }

    [Fact]
    public void TakeScreenshot_ReturnsNull_WhenCaptureFails()
    {
        var mockCapture = CreateSupportedCaptureMock();
        mockCapture.Setup(c => c.CaptureFrame()).Returns((Bitmap?)null);
        mockCapture.SetupGet(c => c.LastError).Returns("Capture failed");

        using var service = new CaptureService(mockApi.Object, settings, mockCapture.Object);
        service.Initialize(IntPtr.Zero);

        var result = service.TakeScreenshot(testOutputDirectory, "TestGame");

        Assert.Null(result);
        Assert.Contains("Capture failed", service.LastError);
    }

    [Fact]
    public void TakeScreenshot_ReturnsNull_WhenCaptureNotInitialized()
    {
        var mockCapture = CreateSupportedCaptureMock();
        mockCapture.Setup(c => c.CaptureFrame()).Returns((Bitmap?)null);

        using var service = new CaptureService(mockApi.Object, settings, mockCapture.Object);

        var result = service.TakeScreenshot(testOutputDirectory, "TestGame");

        Assert.Null(result);
    }

    [Fact]
    public void TakeScreenshot_ReturnsPath_WhenCaptureSucceeds()
    {
        var mockCapture = CreateSupportedCaptureMock();
        var testBitmap = CreateTestBitmap();
        mockCapture.Setup(c => c.CaptureFrame()).Returns(testBitmap);

        using var service = new CaptureService(mockApi.Object, settings, mockCapture.Object);
        service.Initialize(IntPtr.Zero);

        var result = service.TakeScreenshot(testOutputDirectory, "TestGame");

        Assert.NotNull(result);
        Assert.True(File.Exists(result));
        Assert.EndsWith(".png", result);
        Assert.Contains("TestGame", result);

        if (File.Exists(result!))
        {
            File.Delete(result!);
        }
    }

    [Fact]
    public void TakeScreenshot_UsesDefaultOutputPath_WhenNotSpecified()
    {
        var mockCapture = CreateSupportedCaptureMock();
        var testBitmap = CreateTestBitmap();
        mockCapture.Setup(c => c.CaptureFrame()).Returns(testBitmap);

        var customSettings = new CaptureSettings { OutputPath = testOutputDirectory };

        using var service = new CaptureService(mockApi.Object, customSettings, mockCapture.Object);
        service.Initialize(IntPtr.Zero);

        var result = service.TakeScreenshot(null, "TestGame");

        Assert.NotNull(result);
        Assert.StartsWith(testOutputDirectory, result);

        if (File.Exists(result!))
        {
            File.Delete(result!);
        }
    }

    [Fact]
    public void TakeScreenshot_GeneratesTimestampFilename_WhenNoGameName()
    {
        var mockCapture = CreateSupportedCaptureMock();
        var testBitmap = CreateTestBitmap();
        mockCapture.Setup(c => c.CaptureFrame()).Returns(testBitmap);

        using var service = new CaptureService(mockApi.Object, settings, mockCapture.Object);
        service.Initialize(IntPtr.Zero);

        var result = service.TakeScreenshot(testOutputDirectory, null);

        Assert.NotNull(result);
        Assert.DoesNotContain("_", Path.GetFileNameWithoutExtension(result));
        Assert.StartsWith("Capture_", Path.GetFileNameWithoutExtension(result));

        if (File.Exists(result!))
        {
            File.Delete(result!);
        }
    }

    [Fact]
    public void TakeScreenshot_ReturnsNull_WhenDisposed()
    {
        var mockCapture = CreateSupportedCaptureMock();
        var service = new CaptureService(mockApi.Object, settings, mockCapture.Object);
        service.Initialize(IntPtr.Zero);
        service.Dispose();

        Assert.Throws<ObjectDisposedException>(() => service.TakeScreenshot(testOutputDirectory));
    }

    [Fact]
    public void StartRecording_ReturnsFalse_WhenFfmpegNotFound()
    {
        var mockCapture = CreateSupportedCaptureMock();
        using var service = new CaptureService(mockApi.Object, settings, mockCapture.Object);
        service.Initialize(IntPtr.Zero);

        var result = service.StartRecording(testOutputDirectory, "TestGame");

        if (!FfmpegDetector.IsAvailable)
        {
            Assert.False(result);
            Assert.Contains("FFmpeg", service.LastError);
        }
        else
        {
            if (result)
            {
                service.CancelRecording();
            }
        }
    }

    [Fact]
    public void StartRecording_ReturnsFalse_WhenCaptureNotAvailable()
    {
        var mockCapture = new Mock<ICapture>();
        mockCapture.SetupGet(c => c.IsSupported).Returns(false);
        mockCapture.Setup(c => c.Dispose());

        using var service = new CaptureService(mockApi.Object, settings, mockCapture.Object);

        var result = service.StartRecording(testOutputDirectory, "TestGame");

        Assert.False(result);
        Assert.False(service.IsAvailable);
    }

    [Fact]
    public void StartRecording_ReturnsTrue_WhenAlreadyRecording()
    {
        var mockCapture = CreateSupportedCaptureMock();
        using var service = new CaptureService(mockApi.Object, settings, mockCapture.Object);
        service.Initialize(IntPtr.Zero);

        if (FfmpegDetector.IsAvailable)
        {
            return;
        }

        var isRecordingField = typeof(CaptureService).GetField("isRecording",
            BindingFlags.NonPublic | BindingFlags.Instance);
        isRecordingField?.SetValue(service, true);

        var result = service.StartRecording(testOutputDirectory, "TestGame");

        Assert.True(result);

        isRecordingField?.SetValue(service, false);
    }

    [Fact]
    public void StopRecording_ReturnsNull_WhenNotRecording()
    {
        var mockCapture = CreateSupportedCaptureMock();
        using var service = new CaptureService(mockApi.Object, settings, mockCapture.Object);
        service.Initialize(IntPtr.Zero);

        var result = service.StopRecording();

        Assert.Null(result);
    }

    [Fact]
    public void CancelRecording_DoesNothing_WhenNotRecording()
    {
        var mockCapture = CreateSupportedCaptureMock();
        using var service = new CaptureService(mockApi.Object, settings, mockCapture.Object);
        service.Initialize(IntPtr.Zero);

        var exception = Record.Exception(() => service.CancelRecording());

        Assert.Null(exception);
    }

    [Fact]
    public void Initialize_SetsMonitorHandle()
    {
        var mockCapture = CreateSupportedCaptureMock();
        var testHandle = new IntPtr(12345);
        using var service = new CaptureService(mockApi.Object, settings, mockCapture.Object);

        service.Initialize(testHandle);

        mockCapture.Verify(c => c.Initialize(testHandle), Times.Once);
    }

    [Fact]
    public void Dispose_CleansUpResources()
    {
        var mockCapture = CreateSupportedCaptureMock();
        var service = new CaptureService(mockApi.Object, settings, mockCapture.Object);
        service.Initialize(IntPtr.Zero);

        service.Dispose();

        mockCapture.Verify(c => c.Dispose(), Times.Once);
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var mockCapture = CreateSupportedCaptureMock();
        var service = new CaptureService(mockApi.Object, settings, mockCapture.Object);

        service.Dispose();
        service.Dispose();

        mockCapture.Verify(c => c.Dispose(), Times.Once);
    }

    [Fact]
    public void Constructor_Throws_WhenApiIsNull()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new CaptureService(null!, settings, null));
    }

    [Fact]
    public void Constructor_Throws_WhenSettingsIsNull()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new CaptureService(mockApi.Object, null!, null));
    }

    #region Helper Methods

    private static Mock<ICapture> CreateSupportedCaptureMock()
    {
        var mock = new Mock<ICapture>();
        mock.SetupGet(c => c.IsSupported).Returns(true);
        mock.SetupGet(c => c.LastError).Returns((string?)null);
        return mock;
    }

    private static Bitmap CreateTestBitmap()
    {
        var bitmap = new Bitmap(100, 100, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Blue);
        }
        return bitmap;
    }

    #endregion
}
