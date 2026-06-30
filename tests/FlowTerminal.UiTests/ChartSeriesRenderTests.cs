using FlowTerminal.Analytics.Bars;
using FlowTerminal.Charting;
using FlowTerminal.Charting.Overlays;
using SkiaSharp;
using Xunit;

namespace FlowTerminal.UiTests;

/// <summary>
/// Pixel tests for the Batch-1 chart features: OHLC bars, the close line, the volume
/// strip, and the current-price marker. Verifies the green/purple identity holds.
/// </summary>
public class ChartSeriesRenderTests
{
    private static Bar Bar(long open, long high, long low, long close, long volume = 100) =>
        new(BarKind.Time, DateTime.UtcNow, DateTime.UtcNow.AddMinutes(1),
            open, high, low, close, volume, volume / 2, volume / 2, 10);

    private static ChartViewport Viewport(int w = 300, int h = 300) =>
        new(w, h, minPriceTicks: 80, maxPriceTicks: 140, firstBarIndex: 0, visibleBarCount: 4,
            leftPadding: 0, rightAxisWidth: 64, topPadding: 0, bottomPadding: 0);

    private static bool Scan(SKBitmap bmp, Func<SKColor, bool> match, int x0, int x1, int y0, int y1)
    {
        for (int x = x0; x < x1; x++)
        for (int y = y0; y < y1; y++)
        {
            if (match(bmp.GetPixel(x, y))) return true;
        }

        return false;
    }

    [Fact]
    public void Bars_Draw_Green_Up_And_Purple_Down()
    {
        var bars = new[]
        {
            Bar(100, 110, 95, 108),  // bull → green
            Bar(108, 112, 98, 100),  // bear → purple
        };

        using var bmp = new SKBitmap(300, 300);
        using var canvas = new SKCanvas(bmp);
        new BarSeriesRenderer().RenderBars(canvas, Viewport(), bars);

        bool green = Scan(bmp, p => p.Green > p.Red && p.Green > p.Blue && p.Green > 60, 0, 236, 0, 300);
        bool purple = Scan(bmp, p => p.Blue > p.Green && p.Red > p.Green && p.Blue > 80, 0, 236, 0, 300);
        Assert.True(green, "bullish OHLC bar should be green");
        Assert.True(purple, "bearish OHLC bar should be light purple");
    }

    [Fact]
    public void Line_Draws_A_Visible_Series()
    {
        var bars = new[] { Bar(100, 105, 95, 102), Bar(102, 108, 100, 106), Bar(106, 110, 104, 100) };
        using var bmp = new SKBitmap(300, 300);
        using var canvas = new SKCanvas(bmp);
        new BarSeriesRenderer().RenderLine(canvas, Viewport(), bars);

        // Cyan close line: green+blue dominant.
        bool cyan = Scan(bmp, p => p.Blue > 120 && p.Green > 120 && p.Blue >= p.Red, 0, 236, 0, 300);
        Assert.True(cyan, "line chart should draw a visible cyan close line");
    }

    [Fact]
    public void Volume_Strip_Draws_At_The_Bottom()
    {
        var bars = new[] { Bar(100, 110, 95, 108, 500), Bar(108, 112, 98, 100, 800) };
        using var bmp = new SKBitmap(300, 300);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Black);
        new VolumeStripRenderer().Render(canvas, Viewport(), bars);

        // Bars sit in the bottom band; the top of the plot stays clear.
        bool bottom = Scan(bmp, p => p.Red + p.Green + p.Blue > 40, 0, 236, 270, 300);
        bool topClear = !Scan(bmp, p => p.Red + p.Green + p.Blue > 40, 0, 236, 0, 120);
        Assert.True(bottom, "volume strip should draw bars at the bottom of the plot");
        Assert.True(topClear, "volume strip must not paint the upper plot area");
    }

    [Fact]
    public void Price_Marker_Tag_Is_Green_Up_Purple_Down()
    {
        float axisX = 300 - 64;

        using (var bmp = new SKBitmap(300, 300))
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.Black);
            new PriceMarkerRenderer().Render(canvas, Viewport(), priceTicks: 110, bullish: true, tickSize: 0.25m, axisX: axisX);
            bool green = Scan(bmp, p => p.Green > p.Red && p.Green > p.Blue && p.Green > 80, (int)axisX, 300, 0, 300);
            Assert.True(green, "up marker tag should be green");
        }

        using (var bmp = new SKBitmap(300, 300))
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.Black);
            new PriceMarkerRenderer().Render(canvas, Viewport(), priceTicks: 110, bullish: false, tickSize: 0.25m, axisX: axisX);
            bool purple = Scan(bmp, p => p.Blue > p.Green && p.Red > p.Green && p.Blue > 100, (int)axisX, 300, 0, 300);
            Assert.True(purple, "down marker tag should be light purple");
        }
    }
}
