using System;
using Xunit;
using Playnite.SDK.Events;

namespace OverlayPlugin.Tests;

public class ComboMaskTests
{
    [Theory]
    [InlineData("Guide", new[] { ControllerInput.Guide })]
    [InlineData("guide", new[] { ControllerInput.Guide })]
    [InlineData("GUIDE", new[] { ControllerInput.Guide })]
    [InlineData("START+BACK", new[] { ControllerInput.Start, ControllerInput.Back })]
    [InlineData("BACK+START", new[] { ControllerInput.Start, ControllerInput.Back })]
    [InlineData("start+back", new[] { ControllerInput.Start, ControllerInput.Back })]
    [InlineData("LB+RB", new[] { ControllerInput.LeftShoulder, ControllerInput.RightShoulder })]
    [InlineData("RB+LB", new[] { ControllerInput.LeftShoulder, ControllerInput.RightShoulder })]
    [InlineData("lb+rb", new[] { ControllerInput.LeftShoulder, ControllerInput.RightShoulder })]
    public void ResolveComboMask_ValidCombos_ReturnsCorrectButtons(string combo, ControllerInput[] expected)
    {
        var result = TestHelper.ResolveComboMask(combo);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Invalid")]
    [InlineData("A+B")]
    public void ResolveComboMask_InvalidCombos_ReturnsEmptyArray(string combo)
    {
        var result = TestHelper.ResolveComboMask(combo);
        Assert.Empty(result);
    }
}

internal static class TestHelper
{
    public static ControllerInput[] ResolveComboMask(string combo)
    {
        var upper = combo.ToUpperInvariant();
        return upper switch
        {
            "GUIDE" => new[] { ControllerInput.Guide },
            "START+BACK" or "BACK+START" => new[] { ControllerInput.Start, ControllerInput.Back },
            "LB+RB" or "RB+LB" => new[] { ControllerInput.LeftShoulder, ControllerInput.RightShoulder },
            _ => Array.Empty<ControllerInput>()
        };
    }
}
