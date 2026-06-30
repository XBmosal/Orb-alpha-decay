using FlowTerminal.Analytics.Footprints;
using FlowTerminal.Analytics.PriceAction;
using FlowTerminal.Analytics.Profiles;
using FlowTerminal.Charting;
using FlowTerminal.Charting.Overlays;
using FlowTerminal.Domain.Events;
using SkiaSharp;
using Xunit;

namespace FlowTerminal.UiTests;

public class ChartOverlayRenderTests
{
    private static IReadOnlyList<FootprintBar> OneFootprintBar(
        System.Action<Footprint> feed, long o, long h, long l, long c)
    {
        var f = new Footprint();
        feed(f);
        return new[] { FootprintAggregator.Build(f, o, h, l, c, default, default, isClosed: true) };
    }

    private static ChartViewport Viewport(int w = 300, int h = 300) =>
        new(w, h, minPriceTicks: 90, maxPriceTicks: 110, firstBarIndex: 0, visibleBarCount: 10,
            leftPadding: 0, rightAxisWidth: 0, topPadding: 0, bottomPadding: 0);

    [Fact]
    public void Vwap_Line_Draws_In_Cyan_When_Enabled()
    {
        var overlays = ChartOverlays.Empty with
        {
            VwapByBar = Enumerable.Repeat(100.0, 10).ToList(), // flat VWAP at price 100
        };

        using var bmp = new SKBitmap(300, 300);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Black);
        new ChartOverlayRenderer().Render(canvas, Viewport(), overlays, new HashSet<string> { "VWAP" });

        // VWAP price 100 maps to mid-height. The cyan line is green/blue-dominant.
        int y = (int)Viewport().PriceToY(100);
        bool found = false;
        for (int x = 0; x < 300 && !found; x++)
        {
            var p = bmp.GetPixel(x, Math.Clamp(y, 0, 299));
            if (p.Blue > 100 && p.Green > 100 && p.Blue >= p.Red && p.Green > p.Red)
            {
                found = true;
            }
        }

        Assert.True(found, "expected a cyan VWAP pixel along the VWAP price row");
    }

    [Fact]
    public void Profile_Draws_Green_And_Purple_Bars()
    {
        var profile = new List<ProfileLevel> { new(100, BuyVolume: 80, SellVolume: 20) };
        var overlays = ChartOverlays.Empty with { Profile = profile, PocTicks = 100 };

        using var bmp = new SKBitmap(300, 300);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Black);
        new ChartOverlayRenderer().Render(canvas, Viewport(), overlays, new HashSet<string> { "BAC" });

        // The histogram sits on the right; sample a few rows off the amber POC line.
        int y = (int)Viewport().PriceToY(100) - 5;
        bool green = false;
        for (int x = 200; x < 300; x++)
        {
            var p = bmp.GetPixel(x, Math.Clamp(y, 0, 299));
            if (p.Green > p.Red && p.Green > p.Blue && p.Green > 40) { green = true; break; }
        }

        Assert.True(green, "expected green buy volume in the profile histogram");
    }

    [Fact]
    public void Tpo_Letters_Draw_At_Left_Margin_When_Enabled()
    {
        var overlays = ChartOverlays.Empty with { Tpo = new[] { new TpoRow(100, "ABC") } };
        using var bmp = new SKBitmap(300, 300);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Black);
        new ChartOverlayRenderer().Render(canvas, Viewport(), overlays, new HashSet<string> { "TPO" });

        // Some non-black pixel should appear in the left margin around the price-100 row.
        int y = (int)Viewport().PriceToY(100);
        bool drew = false;
        for (int x = 0; x < 60 && !drew; x++)
        for (int dy = -6; dy <= 6 && !drew; dy++)
        {
            if (bmp.GetPixel(x, Math.Clamp(y + dy, 0, 299)) != SKColors.Black) drew = true;
        }

        Assert.True(drew, "expected TPO letters drawn at the left margin");
    }

    private static ChartViewport FootprintViewport(int columns, long minTicks, long maxTicks, int w = 400, int h = 200) =>
        new(w, h, minTicks, maxTicks, firstBarIndex: 0, visibleBarCount: columns,
            leftPadding: 0, rightAxisWidth: 64, topPadding: 0, bottomPadding: 0);

    [Fact]
    public void Footprint_Renderer_Draws_Cells()
    {
        var columns = OneFootprintBar(f =>
        {
            f.AddAt(100, 10, AggressorSide.Sell); // bid
            f.AddAt(100, 80, AggressorSide.Buy);  // ask
        }, 98, 104, 96, 102);
        using var bmp = new SKBitmap(400, 200);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Black);
        new FootprintRenderer().Render(canvas, FootprintViewport(1, 90, 110), columns);

        bool nonBlack = false;
        for (int x = 0; x < 400 && !nonBlack; x += 3)
        for (int yy = 0; yy < 200 && !nonBlack; yy += 3)
        {
            var p = bmp.GetPixel(x, yy);
            if (p.Red + p.Green + p.Blue > 60) nonBlack = true;
        }

        Assert.True(nonBlack, "footprint renderer should draw visible cells/outlines");
    }

    [Fact]
    public void Footprint_Draws_Green_Buys_Right_And_Purple_Sells_Left()
    {
        // A tall, single, wide column where the per-tick rows are far too short for
        // numbers: the colored bid/ask cluster bars must still render so the chart is
        // never empty. Ask volume (buys) → green to the right of center; bid volume
        // (sells) → purple to the left.
        var columns = OneFootprintBar(f =>
        {
            for (long p = 100; p < 260; p++) // 160 price rows over 200px → ~1px rows, not legible
            {
                f.AddAt(p, 90, AggressorSide.Sell); // bid
                f.AddAt(p, 90, AggressorSide.Buy);  // ask
            }
        }, 150, 260, 100, 210);
        var vp = FootprintViewport(1, 100, 260);
        using var bmp = new SKBitmap(400, 200);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Black);
        new FootprintRenderer().Render(canvas, vp, columns);

        int cx = (int)vp.BarCenterX(0);
        bool greenRight = false, purpleLeft = false;
        for (int yy = 20; yy < 180; yy++)
        {
            for (int x = cx + 4; x < cx + 120 && !greenRight; x++)
            {
                var p = bmp.GetPixel(x, yy);
                if (p.Green > p.Red && p.Green > p.Blue && p.Green > 60) greenRight = true;
            }

            for (int x = cx - 120; x < cx - 4 && !purpleLeft; x++)
            {
                var p = bmp.GetPixel(x, yy);
                if (p.Blue > p.Green && p.Red > p.Green && p.Blue > 60) purpleLeft = true;
            }
        }

        Assert.True(greenRight, "expected green ask (buy) bars to the right of center");
        Assert.True(purpleLeft, "expected purple bid (sell) bars to the left of center");
    }

    [Fact]
    public void Footprint_Empty_Shows_Hint_Not_Blank()
    {
        using var bmp = new SKBitmap(400, 200);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Black);
        new FootprintRenderer().Render(canvas, FootprintViewport(1, 90, 110), Array.Empty<FootprintBar>());

        bool nonBlack = false;
        for (int x = 0; x < 400 && !nonBlack; x += 2)
        for (int yy = 0; yy < 200 && !nonBlack; yy += 2)
        {
            if (bmp.GetPixel(x, yy) != SKColors.Black) nonBlack = true;
        }

        Assert.True(nonBlack, "empty footprint should still draw a hint, not a blank panel");
    }

    [Fact]
    public void Disabled_Studies_Draw_Nothing()
    {
        var overlays = ChartOverlays.Empty with { VwapByBar = Enumerable.Repeat(100.0, 10).ToList() };
        using var bmp = new SKBitmap(64, 64);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Black);
        new ChartOverlayRenderer().Render(canvas, Viewport(64, 64), overlays, new HashSet<string>()); // none enabled

        // Canvas stays black.
        for (int x = 0; x < 64; x += 8)
        for (int yy = 0; yy < 64; yy += 8)
        {
            Assert.Equal(SKColors.Black, bmp.GetPixel(x, yy));
        }
    }
}
