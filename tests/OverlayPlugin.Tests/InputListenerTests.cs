using Xunit;
using PlayniteOverlay;

namespace OverlayPlugin.Tests;

public class InputListenerTests
{
    [Fact(Skip = "Placeholder until PlayniteSDK and XInput available")]
    public void Toggle_RaisesEvent()
    {
        var listener = new InputListener();
        var raised = false;
        listener.ToggleRequested += (_, __) => raised = true;
        listener.TriggerToggle();
        Assert.True(raised);
    }
}

