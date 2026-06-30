using FlowTerminal.Charting;
using FlowTerminal.Charting.Drawings;
using SkiaSharp;
using Xunit;

namespace FlowTerminal.UiTests;

/// <summary>Pixel tests for the chart drawing tools (cyan lines, amber Fibonacci).</summary>
public class DrawingRenderTests
{
    private static readonly DateTime TA = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime TB = TA.AddMinutes(1);

    private static ChartViewport Viewport() =>
        new(300, 300, minPriceTicks: 80, maxPriceTicks: 120, firstBarIndex: 0, visibleBarCount: 4,
            leftPadding: 0, rightAxisWidth: 0, topPadding: 0, bottomPadding: 0);

    private static float TimeToX(DateTime t) => t == TA ? 60f : 240f;

    private static bool Scan(SKBitmap bmp, Func<SKColor, bool> m)
    {
        for (int x = 0; x < bmp.Width; x++)
        for (int y = 0; y < bmp.Height; y++)
        {
            if (m(bmp.GetPixel(x, y))) return true;
        }

        return false;
    }

    [Fact]
    public void Trendline_Draws_Cyan()
    {
        var d = new ChartDrawing(DrawingTool.Trendline, new AnchorPoint(TA, 90), new AnchorPoint(TB, 110));
        using var bmp = new SKBitmap(300, 300);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Black);
        new DrawingRenderer().Render(canvas, Viewport(), d, TimeToX, 0.25m);

        Assert.True(Scan(bmp, p => p.Blue > 120 && p.Green > 120 && p.Blue >= p.Red), "trendline should be cyan");
    }

    [Fact]
    public void Fibonacci_Draws_Amber_Levels()
    {
        var d = new ChartDrawing(DrawingTool.Fibonacci, new AnchorPoint(TA, 118), new AnchorPoint(TB, 82));
        using var bmp = new SKBitmap(300, 300);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Black);
        new DrawingRenderer().Render(canvas, Viewport(), d, TimeToX, 0.25m);

        Assert.True(Scan(bmp, p => p.Red > 150 && p.Green > 100 && p.Blue < 110), "fib levels should be amber");
    }

    [Fact]
    public void HorizontalLine_Spans_At_Price_Row()
    {
        var d = new ChartDrawing(DrawingTool.HorizontalLine, new AnchorPoint(TA, 100), new AnchorPoint(TA, 100));
        var vp = Viewport();
        using var bmp = new SKBitmap(300, 300);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Black);
        new DrawingRenderer().Render(canvas, vp, d, TimeToX, 0.25m);

        int y = (int)vp.PriceToY(100);
        bool found = false;
        for (int x = 10; x < 200 && !found; x++)
        {
            var p = bmp.GetPixel(x, Math.Clamp(y, 0, 299));
            if (p.Blue > 120 && p.Green > 120) found = true;
        }

        Assert.True(found, "horizontal line should span the plot at its price row");
    }
}
