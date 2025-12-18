using Xunit;
using PlayniteOverlay.Services;
using PlayniteOverlay.Models;
using Moq;
using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OverlayPlugin.Tests;

public class GameSwitcherTests
{
    [Fact]
    public void SetActiveFromGame_WithGame_SetsActiveAppTitle()
    {
        // Arrange
        var mockApi = new Mock<IPlayniteAPI>();
        var switcher = new GameSwitcher(mockApi.Object);
        var game = new Game("Test Game");

        // Act
        switcher.SetActiveFromGame(game);

        // Assert
        Assert.Equal("Test Game", switcher.ActiveApp?.Title);
    }

    [Fact]
    public void ClearActiveApp_ClearsActiveApp()
    {
        // Arrange
        var mockApi = new Mock<IPlayniteAPI>();
        var switcher = new GameSwitcher(mockApi.Object);
        var game = new Game("Test Game");
        switcher.SetActiveFromGame(game);

        // Act
        switcher.ClearActiveApp();

        // Assert
        Assert.Null(switcher.ActiveApp);
    }

    [Fact]
    public void ClearActiveApp_AfterSettingGame_ClearsTitle()
    {
        // Arrange
        var mockApi = new Mock<IPlayniteAPI>();
        var switcher = new GameSwitcher(mockApi.Object);
        var game = new Game("Test Game");
        switcher.SetActiveFromGame(game);

        // Act
        switcher.ClearActiveApp();

        // Assert
        Assert.Null(switcher.ActiveApp?.Title);
    }

    [Fact]
    public void StartGame_CallsApiStartGame()
    {
        // Arrange
        var mockApi = new Mock<IPlayniteAPI>();
        var switcher = new GameSwitcher(mockApi.Object);
        var gameId = Guid.NewGuid();

        // Act
        switcher.StartGame(gameId);

        // Assert
        mockApi.Verify(a => a.StartGame(gameId), Times.Once);
    }

    [Fact]
    public void ResolveImagePath_WithNull_ReturnsNull()
    {
        // Arrange
        var mockApi = new Mock<IPlayniteAPI>();
        var switcher = new GameSwitcher(mockApi.Object);

        // Act
        var result = switcher.ResolveImagePath(null);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ResolveImagePath_WithEmpty_ReturnsNull()
    {
        // Arrange
        var mockApi = new Mock<IPlayniteAPI>();
        var switcher = new GameSwitcher(mockApi.Object);

        // Act
        var result = switcher.ResolveImagePath("");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ResolveImagePath_WithWhitespace_ReturnsNull()
    {
        // Arrange
        var mockApi = new Mock<IPlayniteAPI>();
        var switcher = new GameSwitcher(mockApi.Object);

        // Act
        var result = switcher.ResolveImagePath("   ");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ResolveImagePath_WithHttpUrl_ReturnsUrlUnmodified()
    {
        // Arrange
        var mockApi = new Mock<IPlayniteAPI>();
        var switcher = new GameSwitcher(mockApi.Object);
        var url = "http://example.com/image.jpg";

        // Act
        var result = switcher.ResolveImagePath(url);

        // Assert
        Assert.Equal(url, result);
    }

    [Fact]
    public void ResolveImagePath_WithHttpsUrl_ReturnsUrlUnmodified()
    {
        // Arrange
        var mockApi = new Mock<IPlayniteAPI>();
        var switcher = new GameSwitcher(mockApi.Object);
        var url = "https://example.com/image.jpg";

        // Act
        var result = switcher.ResolveImagePath(url);

        // Assert
        Assert.Equal(url, result);
    }

    [Fact]
    public void ResolveImagePath_WithLocalPath_CallsGetFullFilePath()
    {
        // Arrange
        var mockApi = new Mock<IPlayniteAPI>();
        var mockDatabase = new Mock<IGameDatabaseAPI>();
        mockApi.Setup(a => a.Database).Returns(mockDatabase.Object);
        mockDatabase.Setup(d => d.GetFullFilePath("local.jpg")).Returns("/full/path/local.jpg");
        var switcher = new GameSwitcher(mockApi.Object);

        // Act
        var result = switcher.ResolveImagePath("local.jpg");

        // Assert
        Assert.Equal("/full/path/local.jpg", result);
        mockDatabase.Verify(d => d.GetFullFilePath("local.jpg"), Times.Once);
    }

    [Fact]
    public void ExitActiveApp_WithNoActiveApp_ShowsNotification()
    {
        // Arrange
        var mockApi = new Mock<IPlayniteAPI>();
        var mockNotifications = new Mock<INotificationsAPI>();
        mockApi.Setup(a => a.Notifications).Returns(mockNotifications.Object);
        var switcher = new GameSwitcher(mockApi.Object);

        // Act & Assert - Should not throw
        switcher.ExitActiveApp();

        // Verify notification was shown
        mockNotifications.Verify(
            n => n.Add(It.IsAny<string>(), It.IsAny<string>(), NotificationType.Info),
            Times.Once);
    }

    [Fact]
    public void ExitActiveApp_WithActiveApp_DoesNotThrow()
    {
        // Arrange
        var mockApi = new Mock<IPlayniteAPI>();
        var mockMainView = new Mock<IMainViewAPI>();
        mockApi.Setup(a => a.MainView).Returns(mockMainView.Object);
        var switcher = new GameSwitcher(mockApi.Object);
        var game = new Game("Test Game");
        switcher.SetActiveFromGame(game);

        // Act & Assert - Should not throw
        switcher.ExitActiveApp();
    }

    [Fact]
    public void ActiveApp_Initially_IsNull()
    {
        // Arrange
        var mockApi = new Mock<IPlayniteAPI>();
        var switcher = new GameSwitcher(mockApi.Object);

        // Assert
        Assert.Null(switcher.ActiveApp);
    }

    [Fact]
    public void SetActiveApp_WithNull_ClearsActiveApp()
    {
        // Arrange
        var mockApi = new Mock<IPlayniteAPI>();
        var switcher = new GameSwitcher(mockApi.Object);
        var app = new RunningApp { Title = "Test App", ProcessId = 1234 };
        switcher.SetActiveApp(app);

        // Act
        switcher.SetActiveApp(null);

        // Assert
        Assert.Null(switcher.ActiveApp);
    }

    [Fact]
    public void SetActiveApp_WithRunningApp_SetsActiveApp()
    {
        // Arrange
        var mockApi = new Mock<IPlayniteAPI>();
        var switcher = new GameSwitcher(mockApi.Object);
        var app = new RunningApp
        {
            Title = "Test App",
            ProcessId = 1234,
            Type = AppType.GenericApp
        };

        // Act
        switcher.SetActiveApp(app);

        // Assert
        Assert.NotNull(switcher.ActiveApp);
        Assert.Equal("Test App", switcher.ActiveApp.Title);
        Assert.Equal(1234, switcher.ActiveApp.ProcessId);
    }

    [Fact]
    public void IsActiveAppStillValid_WithNullActiveApp_ReturnsFalse()
    {
        // Arrange
        var mockApi = new Mock<IPlayniteAPI>();
        var switcher = new GameSwitcher(mockApi.Object);

        // Act
        var result = switcher.IsActiveAppStillValid();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void GetRelativeTime_WithNull_ReturnsNever()
    {
        // Arrange
        var mockApi = new Mock<IPlayniteAPI>();
        var switcher = new GameSwitcher(mockApi.Object);

        // Act
        var result = switcher.GetRelativeTime(null);

        // Assert
        Assert.Equal("never", result);
    }

    [Fact]
    public void GetRelativeTime_WithRecentTime_ReturnsJustNow()
    {
        // Arrange
        var mockApi = new Mock<IPlayniteAPI>();
        var switcher = new GameSwitcher(mockApi.Object);

        // Act
        var result = switcher.GetRelativeTime(DateTime.Now);

        // Assert
        Assert.Equal("just now", result);
    }

    [Fact]
    public void GetRelativeTime_WithYesterday_ReturnsYesterday()
    {
        // Arrange
        var mockApi = new Mock<IPlayniteAPI>();
        var switcher = new GameSwitcher(mockApi.Object);

        // Act
        var result = switcher.GetRelativeTime(DateTime.Now.AddDays(-1.5));

        // Assert
        Assert.Equal("yesterday", result);
    }

    [Fact]
    public void GetSessionDuration_WithNull_ReturnsZeroMinutes()
    {
        // Arrange
        var mockApi = new Mock<IPlayniteAPI>();
        var switcher = new GameSwitcher(mockApi.Object);

        // Act
        var result = switcher.GetSessionDuration(null);

        // Assert
        Assert.Equal("0m", result);
    }

    [Fact]
    public void GetSessionDuration_WithRecentStart_ReturnsLessThanOneMinute()
    {
        // Arrange
        var mockApi = new Mock<IPlayniteAPI>();
        var switcher = new GameSwitcher(mockApi.Object);

        // Act
        var result = switcher.GetSessionDuration(DateTime.Now);

        // Assert
        Assert.Equal("< 1m", result);
    }

    [Fact]
    public void FormatPlaytime_WithZero_ReturnsNotPlayed()
    {
        // Arrange
        var mockApi = new Mock<IPlayniteAPI>();
        var switcher = new GameSwitcher(mockApi.Object);

        // Act
        var result = switcher.FormatPlaytime(0);

        // Assert
        Assert.Equal("Not played", result);
    }

    [Fact]
    public void FormatPlaytime_WithLessThanHour_ReturnsLessThanOneHour()
    {
        // Arrange
        var mockApi = new Mock<IPlayniteAPI>();
        var switcher = new GameSwitcher(mockApi.Object);

        // Act
        var result = switcher.FormatPlaytime(1800); // 30 minutes

        // Assert
        Assert.Equal("< 1 hour", result);
    }

    [Fact]
    public void FormatPlaytime_WithHours_ReturnsFormattedHours()
    {
        // Arrange
        var mockApi = new Mock<IPlayniteAPI>();
        var switcher = new GameSwitcher(mockApi.Object);

        // Act
        var result = switcher.FormatPlaytime(36000); // 10 hours

        // Assert
        Assert.Equal("10.0 hours", result);
    }

    [Fact]
    public void FormatPlaytime_WithManyHours_ReturnsFormattedWithCommas()
    {
        // Arrange
        var mockApi = new Mock<IPlayniteAPI>();
        var switcher = new GameSwitcher(mockApi.Object);

        // Act
        var result = switcher.FormatPlaytime(3600000); // 1000 hours

        // Assert
        Assert.Equal("1,000 hours", result);
    }

    [Fact]
    public void ResolveGame_WithValidId_ReturnsGame()
    {
        // Arrange
        var mockApi = new Mock<IPlayniteAPI>();
        var mockDatabase = new Mock<IGameDatabaseAPI>();
        var mockGames = new Mock<IItemCollection<Game>>();
        var gameId = Guid.NewGuid();
        var game = new Game("Test Game") { Id = gameId };

        mockApi.Setup(a => a.Database).Returns(mockDatabase.Object);
        mockDatabase.Setup(d => d.Games).Returns(mockGames.Object);
        mockGames.Setup(g => g[gameId]).Returns(game);

        var switcher = new GameSwitcher(mockApi.Object);

        // Act
        var result = switcher.ResolveGame(gameId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Test Game", result.Name);
    }

    [Fact]
    public void ResolveGame_WithInvalidId_ReturnsNull()
    {
        // Arrange
        var mockApi = new Mock<IPlayniteAPI>();
        var mockDatabase = new Mock<IGameDatabaseAPI>();
        var mockGames = new Mock<IItemCollection<Game>>();
        var gameId = Guid.NewGuid();

        mockApi.Setup(a => a.Database).Returns(mockDatabase.Object);
        mockDatabase.Setup(d => d.Games).Returns(mockGames.Object);
        mockGames.Setup(g => g[gameId]).Returns(default(Game));

        var switcher = new GameSwitcher(mockApi.Object);

        // Act
        var result = switcher.ResolveGame(gameId);

        // Assert
        Assert.Null(result);
    }
}
