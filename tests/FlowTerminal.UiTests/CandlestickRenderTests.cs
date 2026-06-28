using FlowTerminal.Analytics.Bars;
using FlowTerminal.Charting;
using SkiaSharp;
using Xunit;

namespace FlowTerminal.UiTests;

/// <summary>
/// Pixel-level verification of the mandatory candle colors. These render real
/// candles with SkiaSharp (headless) and sample the resulting bitmap — the
/// strongest form of the "bullish green / bearish light-purple" acceptance test.
/// </summary>
public class CandlestickRenderTests
{
    private static Bar Bar(long open, long high, long low, long close) =>
        new(BarKind.Time, DateTime.UnixEpoch, DateTime.UnixEpoch, open, high, low, close, 100, 60, 40, 10);

    private static SKColor RenderAndSampleBody(Bar bar)
    {
        const int w = 200, h = 200;
        using var bmp = new SKBitmap(w, h);
        using var canvas = new SKCanvas(bmp);
        using var renderer = new CandlestickRenderer();

        var viewport = new ChartViewport(
            width: w, height: h,
            minPriceTicks: bar.LowTicks - 5, maxPriceTicks: bar.HighTicks + 5,
            firstBarIndex: 0, visibleBarCount: 1,
            leftPadding: 0, rightAxisWidth: 0, topPadding: 0, bottomPadding: 0);

        renderer.Render(canvas, viewport, new[] { bar });

        // Sample the center of the candle body.
        float cx = viewport.BarCenterX(0);
        float yMid = viewport.PriceToY((bar.OpenTicks + bar.CloseTicks) / 2);
        return bmp.GetPixel((int)cx, (int)yMid);
    }

    [Fact]
    public void Bullish_Candle_Renders_Green()
    {
        var color = RenderAndSampleBody(Bar(100, 112, 98, 110)); // close > open
        Assert.Equal((byte)0x22, color.Red);
        Assert.Equal((byte)0xC5, color.Green);
        Assert.Equal((byte)0x5E, color.Blue);
    }

    [Fact]
    public void Bearish_Candle_Renders_Light_Purple()
    {
        var color = RenderAndSampleBody(Bar(110, 112, 98, 100)); // close < open
        Assert.Equal((byte)0xC4, color.Red);
        Assert.Equal((byte)0xA7, color.Green);
        Assert.Equal((byte)0xFF, color.Blue);
    }

    [Fact]
    public void Bearish_Candle_Is_Not_Red()
    {
        var color = RenderAndSampleBody(Bar(110, 112, 98, 100));
        // Red would be R-dominant with low blue; purple is blue-dominant.
        Assert.True(color.Blue > color.Red, "Bearish candle must be purple (blue-dominant), never red.");
    }

    [Fact]
    public void Body_Color_Selector_Follows_Open_Close()
    {
        var p = ChartPalette.Default;
        Assert.Equal(p.BullishCandle, CandlestickRenderer.BodyColor(p, 100, 105));
        Assert.Equal(p.BullishCandle, CandlestickRenderer.BodyColor(p, 100, 100)); // doji counts bullish
        Assert.Equal(p.BearishCandle, CandlestickRenderer.BodyColor(p, 105, 100));
    }
}
