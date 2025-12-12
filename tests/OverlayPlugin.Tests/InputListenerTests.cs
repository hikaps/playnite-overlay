using Xunit;
using PlayniteOverlay.Input;
using System;

namespace OverlayPlugin.Tests;

public class InputListenerTests
{
    [Fact]
    public void TriggerToggle_RaisesToggleRequestedEvent()
    {
        // Arrange
        var listener = new InputListener();
        var eventRaised = false;
        listener.ToggleRequested += (_, _) => eventRaised = true;

        // Act
        listener.TriggerToggle();

        // Assert
        Assert.True(eventRaised, "ToggleRequested event should be raised");
    }

    [Fact]
    public void TriggerToggle_RaisesEventWithCorrectSender()
    {
        // Arrange
        var listener = new InputListener();
        object? capturedSender = null;
        listener.ToggleRequested += (sender, _) => capturedSender = sender;

        // Act
        listener.TriggerToggle();

        // Assert
        Assert.Same(listener, capturedSender);
    }

    [Fact]
    public void TriggerToggle_RaisesEventWithEmptyEventArgs()
    {
        // Arrange
        var listener = new InputListener();
        EventArgs? capturedArgs = null;
        listener.ToggleRequested += (_, args) => capturedArgs = args;

        // Act
        listener.TriggerToggle();

        // Assert
        Assert.NotNull(capturedArgs);
        Assert.Same(EventArgs.Empty, capturedArgs);
    }

    [Fact]
    public void MultipleSubscribers_AllReceiveEvent()
    {
        // Arrange
        var listener = new InputListener();
        var subscriber1Called = false;
        var subscriber2Called = false;
        listener.ToggleRequested += (_, _) => subscriber1Called = true;
        listener.ToggleRequested += (_, _) => subscriber2Called = true;

        // Act
        listener.TriggerToggle();

        // Assert
        Assert.True(subscriber1Called, "First subscriber should receive event");
        Assert.True(subscriber2Called, "Second subscriber should receive event");
    }

    [Fact]
    public void UnsubscribedHandler_DoesNotReceiveEvent()
    {
        // Arrange
        var listener = new InputListener();
        var handlerCalled = false;
        EventHandler handler = (_, _) => handlerCalled = true;
        listener.ToggleRequested += handler;
        listener.ToggleRequested -= handler;

        // Act
        listener.TriggerToggle();

        // Assert
        Assert.False(handlerCalled, "Unsubscribed handler should not receive event");
    }
}
