using System;
using System.Collections.Generic;
using Xunit;
using PlayniteOverlay.Services;
using PlayniteOverlay.Models;

namespace OverlayPlugin.Tests;

internal sealed class TestAchievementSource : IAchievementSource
{
    private readonly bool available;
    private readonly GameAchievementSummary? summary;
    public int IsAvailableCallCount { get; private set; }
    public int GetGameAchievementsCallCount { get; private set; }

    public TestAchievementSource(bool available, GameAchievementSummary? summary = null)
    {
        this.available = available;
        this.summary = summary;
    }

    public bool IsAvailable()
    {
        IsAvailableCallCount++;
        return available;
    }

    public GameAchievementSummary? GetGameAchievements(Guid gameId, int maxRecentUnlocked = 3, int maxLocked = 3)
    {
        GetGameAchievementsCallCount++;
        return summary;
    }
}

public class AchievementSourceTests
{
    [Fact]
    public void Provider_ReturnsFirstAvailableSource()
    {
        // Arrange
        var first = new TestAchievementSource(available: true);
        var second = new TestAchievementSource(available: true);
        var provider = new AchievementSourceProvider(first, second);

        // Act
        var result = provider.GetAvailableSource();

        // Assert
        Assert.Same(first, result);
        Assert.Equal(1, first.IsAvailableCallCount);
        Assert.Equal(0, second.IsAvailableCallCount);
    }

    [Fact]
    public void Provider_FallsBackToSecondSource()
    {
        // Arrange
        var first = new TestAchievementSource(available: false);
        var second = new TestAchievementSource(available: true);
        var provider = new AchievementSourceProvider(first, second);

        // Act
        var result = provider.GetAvailableSource();

        // Assert
        Assert.Same(second, result);
        Assert.Equal(1, first.IsAvailableCallCount);
        Assert.Equal(1, second.IsAvailableCallCount);
    }

    [Fact]
    public void Provider_ReturnsNullWhenNoneAvailable()
    {
        // Arrange
        var first = new TestAchievementSource(available: false);
        var second = new TestAchievementSource(available: false);
        var provider = new AchievementSourceProvider(first, second);

        // Act
        var result = provider.GetAvailableSource();

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void Provider_CachesResult()
    {
        // Arrange
        var source = new TestAchievementSource(available: true);
        var provider = new AchievementSourceProvider(source);

        // Act
        var first = provider.GetAvailableSource();
        var second = provider.GetAvailableSource();

        // Assert
        Assert.Same(first, second);
        Assert.Equal(1, source.IsAvailableCallCount);
    }

    [Fact]
    public void Provider_GetGameAchievements_DelegatesToSource()
    {
        // Arrange
        var expectedSummary = new GameAchievementSummary
        {
            TotalCount = 10,
            UnlockedCount = 5
        };
        var source = new TestAchievementSource(available: true, summary: expectedSummary);
        var provider = new AchievementSourceProvider(source);
        var gameId = Guid.NewGuid();

        // Act
        var result = provider.GetGameAchievements(gameId);

        // Assert
        Assert.Equal(1, source.GetGameAchievementsCallCount);
        Assert.Same(expectedSummary, result);
    }

    [Fact]
    public void Provider_GetGameAchievements_ReturnsNullWhenNoSourceAvailable()
    {
        // Arrange
        var source = new TestAchievementSource(available: false);
        var provider = new AchievementSourceProvider(source);

        // Act
        var result = provider.GetGameAchievements(Guid.NewGuid());

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void AchievementData_IsUnlockedDefaultFalse()
    {
        // Arrange & Act
        var data = new AchievementData();

        // Assert
        Assert.False(data.IsUnlocked);
    }

    [Fact]
    public void AchievementData_IsUnlockedSettable()
    {
        // Arrange
        var data = new AchievementData();

        // Act
        data.IsUnlocked = true;

        // Assert
        Assert.True(data.IsUnlocked);
        Assert.Null(data.DateUnlocked);
    }

    [Fact]
    public void AchievementSummaryBuilder_Build_ReturnsCorrectSummary()
    {
        // Arrange
        var achievements = new List<AchievementData>
        {
            new AchievementData { Name = "A1", IsUnlocked = true, DateUnlocked = DateTime.Now.AddDays(-1) },
            new AchievementData { Name = "A2", IsUnlocked = true, DateUnlocked = DateTime.Now },
            new AchievementData { Name = "A3", IsUnlocked = true, DateUnlocked = DateTime.Now.AddDays(-2) },
            new AchievementData { Name = "A4", IsUnlocked = false },
            new AchievementData { Name = "A5", IsUnlocked = false }
        };

        // Act
        var summary = AchievementSummaryBuilder.Build(achievements, maxRecentUnlocked: 3, maxLocked: 3);

        // Assert
        Assert.Equal(5, summary.TotalCount);
        Assert.Equal(3, summary.UnlockedCount);
        Assert.Equal(2, summary.LockedToShow.Count);
        Assert.True(summary.HasData);
    }

    [Fact]
    public void AchievementSummaryBuilder_Build_RespectsMaxRecentUnlocked()
    {
        // Arrange
        var achievements = new List<AchievementData>
        {
            new AchievementData { Name = "A1", IsUnlocked = true, DateUnlocked = DateTime.Now.AddDays(-4) },
            new AchievementData { Name = "A2", IsUnlocked = true, DateUnlocked = DateTime.Now.AddDays(-3) },
            new AchievementData { Name = "A3", IsUnlocked = true, DateUnlocked = DateTime.Now.AddDays(-2) },
            new AchievementData { Name = "A4", IsUnlocked = true, DateUnlocked = DateTime.Now.AddDays(-1) },
            new AchievementData { Name = "A5", IsUnlocked = true, DateUnlocked = DateTime.Now }
        };

        // Act
        var summary = AchievementSummaryBuilder.Build(achievements, maxRecentUnlocked: 2, maxLocked: 0);

        // Assert
        Assert.Equal(5, summary.UnlockedCount);
        Assert.Equal(2, summary.RecentlyUnlocked.Count);
        Assert.Equal("A5", summary.RecentlyUnlocked[0].Name);
        Assert.Equal("A4", summary.RecentlyUnlocked[1].Name);
    }

    [Fact]
    public void AchievementSummaryBuilder_Build_RespectsMaxLocked()
    {
        // Arrange
        var achievements = new List<AchievementData>
        {
            new AchievementData { Name = "L1", IsUnlocked = false },
            new AchievementData { Name = "L2", IsUnlocked = false },
            new AchievementData { Name = "L3", IsUnlocked = false },
            new AchievementData { Name = "L4", IsUnlocked = false }
        };

        // Act
        var summary = AchievementSummaryBuilder.Build(achievements, maxRecentUnlocked: 3, maxLocked: 2);

        // Assert
        Assert.Equal(0, summary.UnlockedCount);
        Assert.Equal(2, summary.LockedToShow.Count);
    }

    [Fact]
    public void AchievementSummaryBuilder_Build_EmptyList_ReturnsZeroCounts()
    {
        // Arrange
        var achievements = new List<AchievementData>();

        // Act
        var summary = AchievementSummaryBuilder.Build(achievements, maxRecentUnlocked: 3, maxLocked: 3);

        // Assert
        Assert.Equal(0, summary.TotalCount);
        Assert.Equal(0, summary.UnlockedCount);
        Assert.False(summary.HasData);
        Assert.Empty(summary.RecentlyUnlocked);
        Assert.Empty(summary.LockedToShow);
    }

    [Fact]
    public void GameAchievementSummary_PercentComplete_CalculatesCorrectly()
    {
        // Arrange
        var summary = new GameAchievementSummary { TotalCount = 10, UnlockedCount = 3 };

        // Assert
        Assert.Equal(30.0, summary.PercentComplete);
    }

    [Fact]
    public void GameAchievementSummary_PercentComplete_ZeroTotal_ReturnsZero()
    {
        // Arrange
        var summary = new GameAchievementSummary { TotalCount = 0, UnlockedCount = 0 };

        // Assert
        Assert.Equal(0, summary.PercentComplete);
    }
}
