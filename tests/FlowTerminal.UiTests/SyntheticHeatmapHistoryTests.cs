using FlowTerminal.Analytics.BigTrades;
using FlowTerminal.Charting.Heatmap;
using FlowTerminal.Domain.Events;
using FlowTerminal.Domain.Instruments;
using FlowTerminal.MarketData.Synthetic;
using FlowTerminal.OrderBook;
using SkiaSharp;
using Xunit;

namespace FlowTerminal.UiTests;

/// <summary>
/// End-to-end check that the synthetic engine's event stream drives a genuine
/// heatmap history (bands begin on add, end on cancel, vary in intensity) and still
/// renders in the product's green-bid / light-purple-ask identity — never a thermal
/// or rainbow palette. This is the "heatmap renders real book history" guarantee.
/// </summary>
public class SyntheticHeatmapHistoryTests
{
    private static readonly Contract Nq = new(RootSymbol.NQ, QuarterlyMonth.December, 2025);
    private static readonly DateTime Start = new(2024, 6, 3, 14, 0, 0, DateTimeKind.Utc);

    private static (LiquidityHeatmap Heat, List<TradeDot> Trades, MarketByPriceOrderBook Book, BigTradeDetector Big) Drive(int events)
    {
        var gen = new SyntheticSessionGenerator(1, Nq, Start, new SyntheticOptions { Seed = 7, StartPrice = 20_000m });
        var heat = new LiquidityHeatmap(TimeSpan.FromMilliseconds(250));
        var book = new MarketByPriceOrderBook();
        var trades = new List<TradeDot>();
        var big = BigTradeDetector.For(RootSymbol.NQ);

        for (int i = 0; i < events; i++)
        {
            var e = gen.Next();
            book.Apply(e);
            heat.OnClock(e.ExchangeTimestampUtc);
            if (e.Type is MarketEventType.BidUpdate or MarketEventType.AskUpdate)
            {
                heat.OnDepth(e);
            }
            else if (e.Type == MarketEventType.Trade)
            {
                var classified = big.OnTrade(e, book.BestBidTicks, book.BestAskTicks, book.IsValid);
                trades.Add(new TradeDot(e.ExchangeTimestampUtc, e.PriceTicks, e.Quantity, classified.Side));
            }
        }

        big.Flush();
        return (heat, trades, book, big);
    }

    [Fact]
    public void Heatmap_History_Is_Genuine_And_Varied()
    {
        var (heat, _, _, _) = Drive(40_000);
        Assert.True(heat.ColumnCount > 20, "expected a real time history of columns");

        // Collect resting sizes across the recent history: a genuine book is varied,
        // not a flat band of identical values.
        var sizes = new List<long>();
        int from = Math.Max(0, heat.Columns.Count - 60);
        for (int i = from; i < heat.Columns.Count; i++)
        {
            foreach (var v in heat.Columns[i].Bid.Values) sizes.Add(v);
            foreach (var v in heat.Columns[i].Ask.Values) sizes.Add(v);
        }

        Assert.True(sizes.Count > 50);
        Assert.True(sizes.Distinct().Count() > 10, "heatmap liquidity is suspiciously uniform");
        sizes.Sort();
        Assert.True(sizes[^1] > sizes[sizes.Count / 2] * 3, "no intensity structure (no heavy tail)");
    }

    [Fact]
    public void Rendered_Heatmap_Is_Green_And_Purple()
    {
        var (heat, trades, book, big) = Drive(30_000);
        var bigTrades = big.Snapshot(trades.Count > 0 ? trades[^1].Time : Start);

        long lo = long.MaxValue, hi = long.MinValue;
        int from = Math.Max(0, heat.Columns.Count - 80);
        for (int i = from; i < heat.Columns.Count; i++)
        {
            foreach (var k in heat.Columns[i].Bid.Keys) { lo = Math.Min(lo, k); hi = Math.Max(hi, k); }
            foreach (var k in heat.Columns[i].Ask.Keys) { lo = Math.Min(lo, k); hi = Math.Max(hi, k); }
        }

        long pad = Math.Max(4, (hi - lo) / 12);
        using var bmp = new SKBitmap(900, 480);
        using var canvas = new SKCanvas(bmp);
        new BookmapRenderer().Render(canvas, new SKRect(0, 0, 900, 480), heat, trades,
            book.BestBidTicks, book.BestAskTicks, trades.Count > 0 ? trades[^1].PriceTicks : 0,
            lo - pad, hi + pad, 0.25m, bigTrades: bigTrades);

        bool green = false, purple = false, thermalOrange = false;
        for (int x = 0; x < bmp.Width; x += 2)
        for (int y = 0; y < bmp.Height; y += 2)
        {
            var p = bmp.GetPixel(x, y);
            if (p.Green > p.Red && p.Green > p.Blue && p.Green > 60) green = true;
            if (p.Blue > p.Green && p.Red > p.Green && p.Blue > 80) purple = true;
            // Guard the colour freeze: no thermal orange/yellow (high R+G, low B).
            if (p.Red > 180 && p.Green > 120 && p.Green < 200 && p.Blue < 60) thermalOrange = true;
        }

        Assert.True(green, "bid liquidity should render green");
        Assert.True(purple, "ask liquidity should render light purple");
        Assert.False(thermalOrange, "heatmap must not use a thermal/orange palette");

        // Dump a preview for manual eyeballing (ignored by CI assertions).
        var dir = Environment.GetEnvironmentVariable("FT_PREVIEW_DIR");
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
            using var img = SKImage.FromBitmap(bmp);
            using var data = img.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(Path.Combine(dir, "synthetic_heatmap.png"), data.ToArray());
        }
    }
}
