using Xunit;
using PlayniteOverlay.Input;
using PlayniteOverlay;
using Playnite.SDK.Events;
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

    [Fact]
    public void HandleControllerButtonEvent_GuidePressed_TriggersToggle()
    {
        var listener = new InputListener();
        listener.ApplySettings(new OverlaySettings { UseControllerToOpen = true });
        var eventRaised = false;
        listener.ToggleRequested += (_, _) => eventRaised = true;

        listener.HandleControllerButtonEvent(ControllerInput.Guide, ControllerInputState.Pressed, 1);

        Assert.True(eventRaised);
    }

    [Fact]
    public void HandleControllerButtonEvent_GuidePressedWhileDisabled_DoesNotTrigger()
    {
        var listener = new InputListener();
        listener.ApplySettings(new OverlaySettings { UseControllerToOpen = false });
        var eventRaised = false;
        listener.ToggleRequested += (_, _) => eventRaised = true;

        listener.HandleControllerButtonEvent(ControllerInput.Guide, ControllerInputState.Pressed, 1);

        Assert.False(eventRaised);
    }

    [Fact]
    public void HandleControllerButtonEvent_RuntimeDisabled_DoesNotTrigger()
    {
        var listener = new InputListener();
        listener.ApplySettings(new OverlaySettings { UseControllerToOpen = true });
        listener.DisableControllerInput();
        var eventRaised = false;
        listener.ToggleRequested += (_, _) => eventRaised = true;

        listener.HandleControllerButtonEvent(ControllerInput.Guide, ControllerInputState.Pressed, 1);

        Assert.False(eventRaised);
    }

    [Fact]
    public void HandleControllerButtonEvent_NoneButton_DoesNotTrigger()
    {
        var listener = new InputListener();
        listener.ApplySettings(new OverlaySettings { UseControllerToOpen = true });
        var eventRaised = false;
        listener.ToggleRequested += (_, _) => eventRaised = true;

        listener.HandleControllerButtonEvent(ControllerInput.None, ControllerInputState.Pressed, 1);

        Assert.False(eventRaised);
    }

    [Fact]
    public void HandleControllerButtonEvent_ToggleCooldownPreventsRapidFire()
    {
        var listener = new InputListener();
        listener.ApplySettings(new OverlaySettings { UseControllerToOpen = true });
        var toggleCount = 0;
        listener.ToggleRequested += (_, _) => toggleCount++;

        // First press should trigger
        listener.HandleControllerButtonEvent(ControllerInput.Guide, ControllerInputState.Pressed, 1);
        listener.HandleControllerButtonEvent(ControllerInput.Guide, ControllerInputState.Released, 1);

        // Immediate second press should be blocked by 300ms cooldown
        listener.HandleControllerButtonEvent(ControllerInput.Guide, ControllerInputState.Pressed, 1);

        Assert.Equal(1, toggleCount);
    }

    [Fact]
    public void HandleControllerDisconnected_ClearsState()
    {
        var listener = new InputListener();
        listener.ApplySettings(new OverlaySettings { UseControllerToOpen = true });

        // Simulate a non-combo button pressed to establish controller state
        listener.HandleControllerButtonEvent(ControllerInput.A, ControllerInputState.Pressed, 1);

        // Disconnect should clear state for this controller
        listener.HandleControllerDisconnected(1);

        // Now pressing the combo should trigger toggle (no stale state)
        var eventRaised = false;
        listener.ToggleRequested += (_, _) => eventRaised = true;
        listener.HandleControllerButtonEvent(ControllerInput.Guide, ControllerInputState.Pressed, 1);
        Assert.True(eventRaised);
    }
}
