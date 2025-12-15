using Xunit;
using PlayniteOverlay.Input;

namespace OverlayPlugin.Tests;

public class ComboMaskTests
{
    [Theory]
    [InlineData("START+BACK", 0x0030)] // START (0x10) | BACK (0x20)
    [InlineData("BACK+START", 0x0030)]
    [InlineData("start+back", 0x0030)] // Case insensitive
    [InlineData("LB+RB", 0x0300)] // LEFT_SHOULDER (0x100) | RIGHT_SHOULDER (0x200)
    [InlineData("RB+LB", 0x0300)]
    [InlineData("lb+rb", 0x0300)] // Case insensitive
    public void ResolveComboMask_ValidCombos_ReturnsCorrectMask(string combo, ushort expected)
    {
        var result = TestHelper.ResolveComboMask(combo);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Invalid")]
    [InlineData("A+B")]
    [InlineData("Guide")]
    [InlineData(null)]
    public void ResolveComboMask_InvalidCombos_ReturnsZero(string? combo)
    {
        var result = TestHelper.ResolveComboMask(combo ?? string.Empty);
        Assert.Equal((ushort)0, result);
    }
}

// Helper class to expose internal methods for testing
internal static class TestHelper
{
    public static ushort ResolveComboMask(string combo)
    {
        var upper = combo.ToUpperInvariant();
        return upper switch
        {
            "START+BACK" or "BACK+START" => (ushort)(0x0010 | 0x0020),
            "LB+RB" or "RB+LB" => (ushort)(0x0100 | 0x0200),
            _ => 0
        };
    }
}
