using FlowTerminal.Charting;
using FlowTerminal.Charting.Dom;
using FlowTerminal.Charting.Heatmap;
using FlowTerminal.Domain.Events;
using FlowTerminal.Domain.Instruments;
using SkiaSharp;
using Xunit;

namespace FlowTerminal.UiTests;

public class LiquidityHeatmapTests
{
    private static readonly DateTime T0 = new(2024, 6, 3, 14, 0, 0, DateTimeKind.Utc);

    private static MarketEvent Depth(Side side, long price, long size, DateTime ts) => MarketEvent.Quote(
        1, RootSymbol.NQ, "NQZ5", "CME Globex",
        side == Side.Bid ? MarketEventType.BidUpdate : MarketEventType.AskUpdate, ts, ts, side, price, size);

    [Fact]
    public void Depth_Updates_Current_Column_Without_Sealing_Per_Event()
    {
        var hm = new LiquidityHeatmap(TimeSpan.FromMilliseconds(250));
        hm.OnDepth(Depth(Side.Bid, 100, 10, T0));
        hm.OnDepth(Depth(Side.Bid, 100, 25, T0.AddMilliseconds(5)));   // same column, updated
        hm.OnDepth(Depth(Side.Ask, 102, 8, T0.AddMilliseconds(10)));

        Assert.Equal(1, hm.ColumnCount); // no new column yet — not per event
        Assert.Equal(25, hm.Columns[0].BidAt(100));
        Assert.Equal(8, hm.Columns[0].AskAt(102));
    }

    [Fact]
    public void Clock_Seals_New_Columns_And_Persists_Liquidity()
    {
        var hm = new LiquidityHeatmap(TimeSpan.FromMilliseconds(100));
        hm.OnDepth(Depth(Side.Bid, 100, 10, T0));
        hm.OnClock(T0.AddMilliseconds(250)); // crosses ~2 boundaries

        Assert.True(hm.ColumnCount >= 3);
        Assert.Equal(10, hm.Columns[^1].BidAt(100)); // liquidity persisted forward
    }

    [Fact]
    public void Tiles_Are_Marked_Dirty_And_Clearable()
    {
        var hm = new LiquidityHeatmap(TimeSpan.FromMilliseconds(100), tileColumns: 4);
        hm.OnDepth(Depth(Side.Bid, 100, 10, T0));
        Assert.NotEmpty(hm.DirtyTiles);

        hm.ClearDirty();
        Assert.Empty(hm.DirtyTiles);

        hm.OnClock(T0.AddMilliseconds(500)); // adds several columns spanning tiles
        Assert.NotEmpty(hm.DirtyTiles);      // only the changed tiles
    }

    [Fact]
    public void Memory_Is_Bounded_By_MaxColumns()
    {
        var hm = new LiquidityHeatmap(TimeSpan.FromMilliseconds(1), tileColumns: 8, maxColumns: 64);
        hm.OnDepth(Depth(Side.Bid, 100, 10, T0));
        hm.OnClock(T0.AddSeconds(2)); // would create ~2000 columns

        Assert.True(hm.ColumnCount <= 64);
        Assert.True(hm.BaseIndex > 0); // oldest columns were evicted
    }

    [Fact]
    public void Intensity_Respects_Scale_Modes()
    {
        Assert.Equal(0.5, LiquidityHeatmap.Intensity(50, 100, HeatmapScale.Absolute), 6);
        Assert.Equal(0, LiquidityHeatmap.Intensity(0, 100, HeatmapScale.Absolute));
        Assert.Equal(1, LiquidityHeatmap.Intensity(200, 100, HeatmapScale.Absolute)); // clamped
        Assert.True(LiquidityHeatmap.Intensity(10, Math.Log(101), HeatmapScale.Logarithmic) > 0);
    }
}

public class HeatmapRenderTests
{
    private static readonly DateTime T0 = new(2024, 6, 3, 14, 0, 0, DateTimeKind.Utc);

    private static MarketEvent Depth(Side side, long price, long size, DateTime ts) => MarketEvent.Quote(
        1, RootSymbol.NQ, "NQZ5", "CME Globex",
        side == Side.Bid ? MarketEventType.BidUpdate : MarketEventType.AskUpdate, ts, ts, side, price, size);

    [Fact]
    public void Bid_Below_Renders_Green_Ask_Above_Renders_Purple()
    {
        var hm = new LiquidityHeatmap(TimeSpan.FromMilliseconds(100));
        // mid at 100: bid below (price 95), ask above (price 105)
        hm.OnDepth(Depth(Side.Bid, 95, 100, T0));
        hm.OnDepth(Depth(Side.Ask, 105, 100, T0));
        hm.OnClock(T0.AddMilliseconds(400)); // fill several columns

        const int w = 200, h = 200;
        using var bmp = new SKBitmap(w, h);
        using var canvas = new SKCanvas(bmp);
        new HeatmapRenderer().Render(canvas, new SKRect(0, 0, w, h), hm, 90, 110, HeatmapScale.Absolute);

        // y for price 95 (low → near bottom) should be green-dominant; 105 (high → near top) purple.
        var pal = ChartPalette.Default;
        var lowPixel = SamplePriceRow(bmp, 95, 90, 110, h);
        var highPixel = SamplePriceRow(bmp, 105, 90, 110, h);

        Assert.True(lowPixel.Green > lowPixel.Red && lowPixel.Green > lowPixel.Blue, "bid row should be green-dominant");
        Assert.True(highPixel.Blue > 80 && highPixel.Red > 80, "ask row should be light-purple");
    }

    private static SKColor SamplePriceRow(SKBitmap bmp, long price, long minP, long maxP, int h)
    {
        float ch = (float)h / (maxP - minP);
        int y = (int)(h - (price - minP + 0.5f) * ch);
        return bmp.GetPixel(bmp.Width / 2, Math.Clamp(y, 0, h - 1));
    }
}

public class PullStackTrackerTests
{
    private static readonly DateTime T0 = new(2024, 6, 3, 14, 0, 0, DateTimeKind.Utc);
    private static MarketEvent Bid(long price, long size) =>
        MarketEvent.Quote(1, RootSymbol.NQ, "NQZ5", "CME Globex", MarketEventType.BidUpdate, T0, T0, Side.Bid, price, size);

    [Fact]
    public void Tracks_Pulling_And_Stacking()
    {
        var t = new PullStackTracker();
        t.OnDepth(Bid(100, 50));  // new level
        t.OnDepth(Bid(100, 30));  // pulled 20
        t.OnDepth(Bid(100, 70));  // stacked 40

        Assert.Equal(20, t.PulledAt(Side.Bid, 100));
        Assert.Equal(40, t.StackedAt(Side.Bid, 100));
    }

    [Fact]
    public void Counts_Replenishment_After_Depletion()
    {
        var t = new PullStackTracker(depletionThreshold: 1);
        t.OnDepth(Bid(100, 50));
        t.OnDepth(Bid(100, 0));   // depleted
        t.OnDepth(Bid(100, 40));  // replenished (1)
        t.OnDepth(Bid(100, 0));   // depleted again
        t.OnDepth(Bid(100, 30));  // replenished (2)

        Assert.Equal(2, t.ReplenishmentsAt(Side.Bid, 100));
    }
}
