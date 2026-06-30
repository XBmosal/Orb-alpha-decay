using FlowTerminal.Charting.Heatmap;
using FlowTerminal.Domain.Events;
using FlowTerminal.Domain.Instruments;
using SkiaSharp;
using Xunit;

namespace FlowTerminal.UiTests;

/// <summary>Pixel tests for the Bookmap-style liquidity renderer.</summary>
public class BookmapRenderTests
{
    private static readonly DateTime T0 = new(2024, 1, 1, 14, 0, 0, DateTimeKind.Utc);

    private static MarketEvent Depth(MarketEventType type, long price, long qty, DateTime t) =>
        new(1, RootSymbol.NQ, "NQZ5", "CME", type, t, t, priceTicks: price, quantity: qty);

    private static bool Scan(SKBitmap bmp, Func<SKColor, bool> m)
    {
        for (int x = 0; x < bmp.Width; x += 2)
        for (int y = 0; y < bmp.Height; y += 2)
        {
            if (m(bmp.GetPixel(x, y))) return true;
        }

        return false;
    }

    [Fact]
    public void Empty_Heatmap_Draws_Waiting_Hint()
    {
        var heat = new LiquidityHeatmap(TimeSpan.FromMilliseconds(250));
        using var bmp = new SKBitmap(400, 200);
        using var canvas = new SKCanvas(bmp);
        new BookmapRenderer().Render(canvas, new SKRect(0, 0, 400, 200), heat,
            Array.Empty<TradeDot>(), 100, 104, 100, 90, 110, 0.25m);

        Assert.True(Scan(bmp, p => p.Red + p.Green + p.Blue > 30), "empty heatmap should draw a waiting hint");
    }

    [Fact]
    public void Liquidity_And_Trade_Bubbles_Render_Green_And_Purple()
    {
        var heat = new LiquidityHeatmap(TimeSpan.FromMilliseconds(250));
        for (int i = 0; i < 6; i++)
        {
            var t = T0.AddMilliseconds(i * 300);
            heat.OnClock(t);
            heat.OnDepth(Depth(MarketEventType.BidUpdate, 98, 800, t));
            heat.OnDepth(Depth(MarketEventType.AskUpdate, 106, 800, t));
        }

        heat.OnClock(T0.AddMilliseconds(2000));

        var trades = new[]
        {
            new TradeDot(T0.AddMilliseconds(600), 99, 40, AggressorSide.Buy),   // buy → green bubble
            new TradeDot(T0.AddMilliseconds(1200), 105, 40, AggressorSide.Sell) // sell → purple bubble
        };

        using var bmp = new SKBitmap(500, 260);
        using var canvas = new SKCanvas(bmp);
        new BookmapRenderer().Render(canvas, new SKRect(0, 0, 500, 260), heat, trades, 98, 106, 99, 92, 112, 0.25m);

        bool green = Scan(bmp, p => p.Green > p.Red && p.Green > p.Blue && p.Green > 60);
        bool purple = Scan(bmp, p => p.Blue > p.Green && p.Red > p.Green && p.Blue > 80);
        Assert.True(green, "bid liquidity / buy bubbles should be green");
        Assert.True(purple, "ask liquidity / sell bubbles should be light purple");
    }
}
