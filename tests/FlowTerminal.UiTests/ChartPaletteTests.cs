using FlowTerminal.Charting;
using Xunit;

namespace FlowTerminal.UiTests;

/// <summary>
/// Enforces the mandatory product requirement on candle colors. These are numeric
/// assertions on the palette, not screenshot comparisons.
/// </summary>
public class ChartPaletteTests
{
    [Fact]
    public void Bullish_Candle_Is_Green_22C55E()
    {
        var p = ChartPalette.Default;
        Assert.Equal("#22C55E", p.BullishCandle.ToHex());
        Assert.Equal(new RgbaColor(0x22, 0xC5, 0x5E), p.BullishCandle);
    }

    [Fact]
    public void Bearish_Candle_Is_Light_Purple_C4A7FF()
    {
        var p = ChartPalette.Default;
        Assert.Equal("#C4A7FF", p.BearishCandle.ToHex());
        Assert.Equal(new RgbaColor(0xC4, 0xA7, 0xFF), p.BearishCandle);
    }

    [Fact]
    public void Bearish_Default_Is_Not_Red()
    {
        var p = ChartPalette.Default;
        // Red is reserved for critical errors only; the bearish candle must be purple.
        Assert.NotEqual(p.CriticalError, p.BearishCandle);
        Assert.True(p.BearishCandle.B > p.BearishCandle.R, "Bearish color should be blue-dominant (purple), not red.");
    }

    [Fact]
    public void Negative_Delta_Matches_Bearish_Purple_Family()
    {
        var p = ChartPalette.Default;
        Assert.Equal("#C4A7FF", p.NegativeDelta.ToHex());
    }

    [Fact]
    public void Background_Is_Dark_First()
    {
        var bg = ChartPalette.Default.Background;
        Assert.True(bg.R < 32 && bg.G < 32 && bg.B < 32, "Background should be near-black charcoal.");
    }

    [Theory]
    [InlineData("#22C55E", 0x22, 0xC5, 0x5E, 255)]
    [InlineData("#C4A7FF", 0xC4, 0xA7, 0xFF, 255)]
    [InlineData("#80808080", 0x80, 0x80, 0x80, 0x80)]
    public void RgbaColor_Parses_Hex(string hex, int r, int g, int b, int a)
    {
        var c = RgbaColor.FromHex(hex);
        Assert.Equal((byte)r, c.R);
        Assert.Equal((byte)g, c.G);
        Assert.Equal((byte)b, c.B);
        Assert.Equal((byte)a, c.A);
    }
}
