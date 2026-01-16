using Xunit;
using PlayniteOverlay.Services;
using PlayniteOverlay.Models;
using PlayniteOverlay;
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
        var switcher = new GameSwitcher(mockApi.Object, new OverlaySettings());
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
        var switcher = new GameSwitcher(mockApi.Object, new OverlaySettings());
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
        var switcher = new GameSwitcher(mockApi.Object, new OverlaySettings());
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
        var switcher = new GameSwitcher(mockApi.Object, new OverlaySettings());
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
        var switcher = new GameSwitcher(mockApi.Object, new OverlaySettings());

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
        var switcher = new GameSwitcher(mockApi.Object, new OverlaySettings());

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
        var switcher = new GameSwitcher(mockApi.Object, new OverlaySettings());

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
        var switcher = new GameSwitcher(mockApi.Object, new OverlaySettings());
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
        var switcher = new GameSwitcher(mockApi.Object, new OverlaySettings());
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
        var switcher = new GameSwitcher(mockApi.Object, new OverlaySettings());

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
        var switcher = new GameSwitcher(mockApi.Object, new OverlaySettings());

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
        var switcher = new GameSwitcher(mockApi.Object, new OverlaySettings());
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
        var switcher = new GameSwitcher(mockApi.Object, new OverlaySettings());

        // Assert
        Assert.Null(switcher.ActiveApp);
    }

    [Fact]
    public void SetActiveApp_WithNull_ClearsActiveApp()
    {
        // Arrange
        var mockApi = new Mock<IPlayniteAPI>();
        var switcher = new GameSwitcher(mockApi.Object, new OverlaySettings());
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
        var switcher = new GameSwitcher(mockApi.Object, new OverlaySettings());
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
        var switcher = new GameSwitcher(mockApi.Object, new OverlaySettings());

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
        var switcher = new GameSwitcher(mockApi.Object, new OverlaySettings());

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
        var switcher = new GameSwitcher(mockApi.Object, new OverlaySettings());

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
        var switcher = new GameSwitcher(mockApi.Object, new OverlaySettings());

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
        var switcher = new GameSwitcher(mockApi.Object, new OverlaySettings());

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
        var switcher = new GameSwitcher(mockApi.Object, new OverlaySettings());

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
        var switcher = new GameSwitcher(mockApi.Object, new OverlaySettings());

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
        var switcher = new GameSwitcher(mockApi.Object, new OverlaySettings());

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
        var switcher = new GameSwitcher(mockApi.Object, new OverlaySettings());

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
        var switcher = new GameSwitcher(mockApi.Object, new OverlaySettings());

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

        var switcher = new GameSwitcher(mockApi.Object, new OverlaySettings());

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
        mockGames.Setup(g => g[gameId]).Returns((Game?)null);

        var switcher = new GameSwitcher(mockApi.Object, new OverlaySettings());

        // Act
        var result = switcher.ResolveGame(gameId);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetRecentGames_WithNoExclusions_ReturnsAllRecentGames()
    {
        // Arrange
        var mockApi = new Mock<IPlayniteAPI>();
        var mockDatabase = new Mock<IGameDatabaseAPI>();
        var mockGames = new Mock<IItemCollection<Game>>();
        
        var game1 = new Game("Game 1") { Id = Guid.NewGuid(), LastActivity = DateTime.Now.AddHours(-1) };
        var game2 = new Game("Game 2") { Id = Guid.NewGuid(), LastActivity = DateTime.Now.AddHours(-2) };
        var game3 = new Game("Game 3") { Id = Guid.NewGuid(), LastActivity = DateTime.Now.AddHours(-3) };
        
        var games = new List<Game> { game1, game2, game3 };
        mockGames.As<IEnumerable<Game>>().Setup(g => g.GetEnumerator()).Returns(() => games.GetEnumerator());
        
        mockApi.Setup(a => a.Database).Returns(mockDatabase.Object);
        mockDatabase.Setup(d => d.Games).Returns(mockGames.Object);

        var switcher = new GameSwitcher(mockApi.Object, new OverlaySettings());

        // Act
        var result = switcher.GetRecentGames(5).ToList();

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Equal("Game 1", result[0].Name);
        Assert.Equal("Game 2", result[1].Name);
        Assert.Equal("Game 3", result[2].Name);
    }

    [Fact]
    public void GetRecentGames_WithExclusionSet_ExcludesSpecifiedGames()
    {
        // Arrange
        var mockApi = new Mock<IPlayniteAPI>();
        var mockDatabase = new Mock<IGameDatabaseAPI>();
        var mockGames = new Mock<IItemCollection<Game>>();
        
        var game1 = new Game("Game 1") { Id = Guid.NewGuid(), LastActivity = DateTime.Now.AddHours(-1) };
        var game2 = new Game("Game 2") { Id = Guid.NewGuid(), LastActivity = DateTime.Now.AddHours(-2) };
        var game3 = new Game("Game 3") { Id = Guid.NewGuid(), LastActivity = DateTime.Now.AddHours(-3) };
        var game4 = new Game("Game 4") { Id = Guid.NewGuid(), LastActivity = DateTime.Now.AddHours(-4) };
        
        var games = new List<Game> { game1, game2, game3, game4 };
        mockGames.As<IEnumerable<Game>>().Setup(g => g.GetEnumerator()).Returns(() => games.GetEnumerator());
        
        mockApi.Setup(a => a.Database).Returns(mockDatabase.Object);
        mockDatabase.Setup(d => d.Games).Returns(mockGames.Object);

        var switcher = new GameSwitcher(mockApi.Object, new OverlaySettings());
        
        // Exclude game2 and game3 (simulating they are running)
        var excludeIds = new HashSet<Guid> { game2.Id, game3.Id };

        // Act
        var result = switcher.GetRecentGames(5, excludeIds).ToList();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal("Game 1", result[0].Name);
        Assert.Equal("Game 4", result[1].Name);
        Assert.DoesNotContain(result, g => g.Name == "Game 2");
        Assert.DoesNotContain(result, g => g.Name == "Game 3");
    }

    [Fact]
    public void GetRecentGames_WithExclusionSet_ReturnsRequestedCount()
    {
        // Arrange
        var mockApi = new Mock<IPlayniteAPI>();
        var mockDatabase = new Mock<IGameDatabaseAPI>();
        var mockGames = new Mock<IItemCollection<Game>>();
        
        var games = new List<Game>();
        for (int i = 1; i <= 10; i++)
        {
            games.Add(new Game($"Game {i}") 
            { 
                Id = Guid.NewGuid(), 
                LastActivity = DateTime.Now.AddHours(-i) 
            });
        }
        
        mockGames.As<IEnumerable<Game>>().Setup(g => g.GetEnumerator()).Returns(() => games.GetEnumerator());
        
        mockApi.Setup(a => a.Database).Returns(mockDatabase.Object);
        mockDatabase.Setup(d => d.Games).Returns(mockGames.Object);

        var switcher = new GameSwitcher(mockApi.Object, new OverlaySettings());
        
        // Exclude games 2 and 3 (simulating they are running)
        var excludeIds = new HashSet<Guid> { games[1].Id, games[2].Id };

        // Act - request 5 games
        var result = switcher.GetRecentGames(5, excludeIds).ToList();

        // Assert - should still get 5 games (1, 4, 5, 6, 7)
        Assert.Equal(5, result.Count);
        Assert.Equal("Game 1", result[0].Name);
        Assert.Equal("Game 4", result[1].Name);
        Assert.Equal("Game 5", result[2].Name);
        Assert.Equal("Game 6", result[3].Name);
        Assert.Equal("Game 7", result[4].Name);
    }

    [Fact]
    public void GetRecentGames_WithEmptyExclusionSet_ReturnsAllRecentGames()
    {
        // Arrange
        var mockApi = new Mock<IPlayniteAPI>();
        var mockDatabase = new Mock<IGameDatabaseAPI>();
        var mockGames = new Mock<IItemCollection<Game>>();
        
        var game1 = new Game("Game 1") { Id = Guid.NewGuid(), LastActivity = DateTime.Now.AddHours(-1) };
        var game2 = new Game("Game 2") { Id = Guid.NewGuid(), LastActivity = DateTime.Now.AddHours(-2) };
        
        var games = new List<Game> { game1, game2 };
        mockGames.As<IEnumerable<Game>>().Setup(g => g.GetEnumerator()).Returns(() => games.GetEnumerator());
        
        mockApi.Setup(a => a.Database).Returns(mockDatabase.Object);
        mockDatabase.Setup(d => d.Games).Returns(mockGames.Object);

        var switcher = new GameSwitcher(mockApi.Object, new OverlaySettings());

        // Act
        var result = switcher.GetRecentGames(5, new HashSet<Guid>()).ToList();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal("Game 1", result[0].Name);
        Assert.Equal("Game 2", result[1].Name);
    }
}
