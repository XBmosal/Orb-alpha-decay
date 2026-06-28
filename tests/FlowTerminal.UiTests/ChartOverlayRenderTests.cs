using FlowTerminal.Analytics.PriceAction;
using FlowTerminal.Analytics.Profiles;
using FlowTerminal.Charting;
using FlowTerminal.Charting.Overlays;
using SkiaSharp;
using Xunit;

namespace FlowTerminal.UiTests;

public class ChartOverlayRenderTests
{
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
