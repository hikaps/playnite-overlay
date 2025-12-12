using Xunit;
using PlayniteOverlay.Models;
using PlayniteOverlay.Services;
using System;
using Moq;
using Playnite.SDK;

namespace OverlayPlugin.Tests;

public class OverlayItemTests
{
    [Fact]
    public void FromGame_WithValidGame_CreatesItemWithCorrectTitle()
    {
        // Arrange
        var mockApi = new Mock<IPlayniteAPI>();
        var mockDatabase = new Mock<IGameDatabase>();
        mockApi.Setup(a => a.Database).Returns(mockDatabase.Object);
        mockDatabase.Setup(d => d.GetFullFilePath(It.IsAny<string>())).Returns((string path) => path);
        
        var switcher = new GameSwitcher(mockApi.Object);
        var game = new Playnite.SDK.Models.Game("Test Game");

        // Act
        var item = OverlayItem.FromGame(game, switcher);

        // Assert
        Assert.Equal("Test Game", item.Title);
        Assert.Equal(game.Id, item.GameId);
    }

    [Fact]
    public void FromGame_WithCoverImage_UsesCoverImageOverIcon()
    {
        // Arrange
        var mockApi = new Mock<IPlayniteAPI>();
        var mockDatabase = new Mock<IGameDatabase>();
        mockApi.Setup(a => a.Database).Returns(mockDatabase.Object);
        mockDatabase.Setup(d => d.GetFullFilePath("cover.jpg")).Returns("/path/to/cover.jpg");
        
        var switcher = new GameSwitcher(mockApi.Object);
        var game = new Playnite.SDK.Models.Game("Test Game")
        {
            CoverImage = "cover.jpg",
            Icon = "icon.png"
        };

        // Act
        var item = OverlayItem.FromGame(game, switcher);

        // Assert
        Assert.Equal("/path/to/cover.jpg", item.ImagePath);
    }

    [Fact]
    public void FromGame_WithoutCoverImage_UsesIcon()
    {
        // Arrange
        var mockApi = new Mock<IPlayniteAPI>();
        var mockDatabase = new Mock<IGameDatabase>();
        mockApi.Setup(a => a.Database).Returns(mockDatabase.Object);
        mockDatabase.Setup(d => d.GetFullFilePath("icon.png")).Returns("/path/to/icon.png");
        
        var switcher = new GameSwitcher(mockApi.Object);
        var game = new Playnite.SDK.Models.Game("Test Game")
        {
            Icon = "icon.png"
        };

        // Act
        var item = OverlayItem.FromGame(game, switcher);

        // Assert
        Assert.Equal("/path/to/icon.png", item.ImagePath);
    }

    [Fact]
    public void FromGame_OnSelectAction_CallsStartGame()
    {
        // Arrange
        var mockApi = new Mock<IPlayniteAPI>();
        var mockDatabase = new Mock<IGameDatabase>();
        mockApi.Setup(a => a.Database).Returns(mockDatabase.Object);
        
        var switcher = new GameSwitcher(mockApi.Object);
        var game = new Playnite.SDK.Models.Game("Test Game");
        var item = OverlayItem.FromGame(game, switcher);

        // Act
        item.OnSelect?.Invoke();

        // Assert
        mockApi.Verify(a => a.StartGame(game.Id), Times.Once);
    }

    [Fact]
    public void OverlayItem_Properties_CanBeSetAndRetrieved()
    {
        // Arrange & Act
        var item = new OverlayItem
        {
            Title = "Custom Title",
            GameId = Guid.NewGuid(),
            ImagePath = "/custom/path.jpg",
            OnSelect = () => { }
        };

        // Assert
        Assert.Equal("Custom Title", item.Title);
        Assert.NotEqual(Guid.Empty, item.GameId);
        Assert.Equal("/custom/path.jpg", item.ImagePath);
        Assert.NotNull(item.OnSelect);
    }

    [Fact]
    public void OverlayItem_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var item = new OverlayItem();

        // Assert
        Assert.Equal(string.Empty, item.Title);
        Assert.Equal(Guid.Empty, item.GameId);
        Assert.Null(item.ImagePath);
        Assert.Null(item.OnSelect);
    }
}
