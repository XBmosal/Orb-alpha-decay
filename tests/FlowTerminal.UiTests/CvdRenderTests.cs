using FlowTerminal.Charting.Overlays;
using SkiaSharp;
using Xunit;

namespace FlowTerminal.UiTests;

/// <summary>Pixel tests for the CVD sparkline and candlestick renderer (green up / purple down).</summary>
public class CvdRenderTests
{
    private static bool Scan(SKBitmap bmp, Func<SKColor, bool> match)
    {
        for (int x = 0; x < bmp.Width; x += 2)
        for (int y = 0; y < bmp.Height; y += 2)
        {
            if (match(bmp.GetPixel(x, y))) return true;
        }

        return false;
    }

    [Fact]
    public void Line_Renders_A_Visible_Series()
    {
        var bars = new[]
        {
            new CvdBar(0, 50, -10, 40), new CvdBar(40, 60, 30, -20), new CvdBar(-20, 10, -40, -30),
        };
        using var bmp = new SKBitmap(400, 160);
        using var canvas = new SKCanvas(bmp);
        new CvdRenderer().RenderLine(canvas, new SKRect(4, 4, 396, 156), bars);

        Assert.True(Scan(bmp, p => p.Red + p.Green + p.Blue > 40), "CVD line should draw a visible series");
    }

    [Fact]
    public void Candles_Are_Green_Up_And_Purple_Down()
    {
        var bars = new[]
        {
            new CvdBar(0, 110, -5, 100),   // rising → green
            new CvdBar(100, 105, -10, 0),  // falling → purple
        };
        using var bmp = new SKBitmap(400, 200);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Black);
        new CvdRenderer().RenderCandles(canvas, new SKRect(4, 4, 396, 196), bars);

        bool green = Scan(bmp, p => p.Green > p.Red && p.Green > p.Blue && p.Green > 80);
        bool purple = Scan(bmp, p => p.Blue > p.Green && p.Red > p.Green && p.Blue > 100);
        Assert.True(green, "rising CVD candle should be green");
        Assert.True(purple, "falling CVD candle should be light purple");
    }
}
