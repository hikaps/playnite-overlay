using Xunit;
using PlayniteOverlay.Services;
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
    public void SetCurrent_WithGame_SetsCurrentGameTitle()
    {
        // Arrange
        var mockApi = new Mock<IPlayniteAPI>();
        var switcher = new GameSwitcher(mockApi.Object);
        var game = new Game("Test Game");

        // Act
        switcher.SetCurrent(game);

        // Assert
        Assert.Equal("Test Game", switcher.CurrentGameTitle);
    }

    [Fact]
    public void SetCurrent_WithNull_SetsNullTitle()
    {
        // Arrange
        var mockApi = new Mock<IPlayniteAPI>();
        var switcher = new GameSwitcher(mockApi.Object);
        var game = new Game("Test Game");
        switcher.SetCurrent(game);

        // Act
        switcher.SetCurrent(null);

        // Assert
        Assert.Null(switcher.CurrentGameTitle);
    }

    [Fact]
    public void ClearCurrent_AfterSettingGame_ClearsTitle()
    {
        // Arrange
        var mockApi = new Mock<IPlayniteAPI>();
        var switcher = new GameSwitcher(mockApi.Object);
        var game = new Game("Test Game");
        switcher.SetCurrent(game);

        // Act
        switcher.ClearCurrent();

        // Assert
        Assert.Null(switcher.CurrentGameTitle);
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
        var mockDatabase = new Mock<IGameDatabase>();
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
    public void ExitCurrent_WithNoCurrentGame_LogsWarning()
    {
        // Arrange
        var mockApi = new Mock<IPlayniteAPI>();
        var switcher = new GameSwitcher(mockApi.Object);

        // Act & Assert - Should not throw
        switcher.ExitCurrent();
    }

    [Fact]
    public void ExitCurrent_WithCurrentGame_DoesNotThrow()
    {
        // Arrange
        var mockApi = new Mock<IPlayniteAPI>();
        var mockMainView = new Mock<IMainViewAPI>();
        mockApi.Setup(a => a.MainView).Returns(mockMainView.Object);
        var switcher = new GameSwitcher(mockApi.Object);
        var game = new Game("Test Game");
        switcher.SetCurrent(game);

        // Act & Assert - Should not throw
        switcher.ExitCurrent();
    }
}
