using FlowTerminal.Analytics.BigTrades;
using FlowTerminal.Domain.Events;
using FlowTerminal.Domain.Instruments;
using Xunit;

namespace FlowTerminal.Analytics.Tests;

public class BigTradeDetectorTests
{
    private static readonly DateTime T = new(2024, 6, 3, 14, 0, 0, DateTimeKind.Utc);

    private static MarketEvent Tr(double sec, long price, long qty, AggressorSide a) =>
        MarketEvent.Trade(1, RootSymbol.NQ, "NQZ5", "CME", T.AddSeconds(sec), T.AddSeconds(sec),
            price, qty, a, flags: MarketEventFlags.AggressorSupplied);

    private static BigTradeSettings Fixed(long threshold, AggregationMode agg = AggregationMode.None) =>
        new BigTradeSettings { Mode = ThresholdMode.Fixed, FixedBuyThreshold = threshold, FixedSellThreshold = threshold,
            AbsoluteFloor = 1, Aggregation = agg, MinSamples = 1 }.Validate();

    // ── Threshold ────────────────────────────────────────────────────────

    [Fact]
    public void Fixed_Threshold_Selects_Only_Large_Trades()
    {
        var d = new BigTradeDetector(Fixed(50));
        d.OnTrade(Tr(0, 100, 10, AggressorSide.Buy), 99, 101, true);   // below
        d.OnTrade(Tr(1, 100, 50, AggressorSide.Buy), 99, 101, true);   // equal → large
        d.OnTrade(Tr(2, 100, 80, AggressorSide.Sell), 99, 101, true);  // above → large
        d.Flush();

        var groups = d.Snapshot(T.AddSeconds(3));
        Assert.Equal(2, groups.Count);
        Assert.Contains(groups, g => g.TotalQuantity == 50 && g.Side == AggressorSide.Buy);
        Assert.Contains(groups, g => g.TotalQuantity == 80 && g.Side == AggressorSide.Sell);
    }

    [Fact]
    public void Rolling_Percentile_Warms_Up_Then_Flags_Outliers()
    {
        var s = new BigTradeSettings { Mode = ThresholdMode.RollingPercentile, Percentile = 0.95,
            MinSamples = 20, RollingWindow = 200, AbsoluteFloor = 1, Aggregation = AggregationMode.None }.Validate();
        var d = new BigTradeDetector(s);
        // 40 ordinary trades of size 10.
        for (int i = 0; i < 40; i++) d.OnTrade(Tr(i, 100, 10, AggressorSide.Buy), 99, 101, true);
        // One outlier.
        d.OnTrade(Tr(41, 100, 500, AggressorSide.Buy), 99, 101, true);
        d.Flush();

        var groups = d.Snapshot(T.AddSeconds(60));
        Assert.Single(groups);
        Assert.Equal(500, groups[0].TotalQuantity);
    }

    // ── Aggregation ──────────────────────────────────────────────────────

    [Fact]
    public void SamePrice_Aggregation_Sums_Within_Window()
    {
        var s = Fixed(50, AggregationMode.SamePrice) with { TimeWindowMs = 500 };
        var d = new BigTradeDetector(s.Validate());
        d.OnTrade(Tr(0.00, 100, 20, AggressorSide.Buy), 99, 101, true);
        d.OnTrade(Tr(0.10, 100, 20, AggressorSide.Buy), 99, 101, true);
        d.OnTrade(Tr(0.20, 100, 20, AggressorSide.Buy), 99, 101, true);
        d.Flush();

        var g = Assert.Single(d.Snapshot(T.AddSeconds(2)));
        Assert.Equal(60, g.TotalQuantity);
        Assert.Equal(3, g.TradeCount);
        Assert.Equal(20, g.LargestTrade);
        Assert.True(g.IsAggregated);
        Assert.Equal(100, g.PriceTicks);
    }

    [Fact]
    public void Opposite_Side_Splits_The_Group()
    {
        var s = Fixed(30, AggregationMode.AdjacentPrice) with { TimeWindowMs = 500, RequireSameSide = true };
        var d = new BigTradeDetector(s.Validate());
        d.OnTrade(Tr(0.0, 100, 20, AggressorSide.Buy), 99, 101, true);
        d.OnTrade(Tr(0.1, 100, 20, AggressorSide.Sell), 99, 101, true); // side change → split
        d.OnTrade(Tr(0.2, 100, 20, AggressorSide.Sell), 99, 101, true);
        d.Flush();

        var groups = d.Snapshot(T.AddSeconds(2));
        // Buy group = 20 (below 30 → not large); sell group = 40 (large).
        var g = Assert.Single(groups);
        Assert.Equal(AggressorSide.Sell, g.Side);
        Assert.Equal(40, g.TotalQuantity);
    }

    [Fact]
    public void Time_Window_Split_Breaks_Group()
    {
        var s = Fixed(15, AggregationMode.SamePrice) with { TimeWindowMs = 100 };
        var d = new BigTradeDetector(s.Validate());
        d.OnTrade(Tr(0.0, 100, 20, AggressorSide.Buy), 99, 101, true);
        d.OnTrade(Tr(5.0, 100, 20, AggressorSide.Buy), 99, 101, true); // 5s gap >> 100ms → new group
        d.Flush();

        var groups = d.Snapshot(T.AddSeconds(10));
        Assert.Equal(2, groups.Count);
        Assert.All(groups, g => Assert.Equal(20, g.TotalQuantity));
    }

    [Fact]
    public void Weighted_Average_Price_Is_Correct()
    {
        var s = Fixed(30, AggregationMode.AdjacentPrice) with { TimeWindowMs = 500, MaxTickDistance = 4 };
        var d = new BigTradeDetector(s.Validate());
        d.OnTrade(Tr(0.0, 100, 10, AggressorSide.Buy), 99, 101, true);
        d.OnTrade(Tr(0.1, 102, 30, AggressorSide.Buy), 101, 103, true);
        d.Flush();
        // weighted = (100*10 + 102*30) / 40 = (1000+3060)/40 = 101.5 → round 102
        var g = Assert.Single(d.Snapshot(T.AddSeconds(2)));
        Assert.Equal(102, g.PriceTicks);
        Assert.Equal(100, g.MinPriceTicks);
        Assert.Equal(102, g.MaxPriceTicks);
    }

    // ── Sweeps ───────────────────────────────────────────────────────────

    [Fact]
    public void Buy_Sweep_Across_Levels_Is_Flagged()
    {
        var s = new BigTradeSettings { Mode = ThresholdMode.Fixed, FixedBuyThreshold = 1, AbsoluteFloor = 1,
            Aggregation = AggregationMode.Sweep, SweepMinLevels = 3, SweepMinVolume = 30, TimeWindowMs = 500,
            MaxTickDistance = 1, MinSamples = 1 }.Validate();
        var d = new BigTradeDetector(s);
        d.OnTrade(Tr(0.00, 100, 15, AggressorSide.Buy), 99, 100, true);
        d.OnTrade(Tr(0.05, 101, 15, AggressorSide.Buy), 100, 101, true);
        d.OnTrade(Tr(0.10, 102, 15, AggressorSide.Buy), 101, 102, true);
        d.Flush();

        var g = Assert.Single(d.Snapshot(T.AddSeconds(2)));
        Assert.True(g.IsSweep);
        Assert.Equal(3, g.SweepLevels);
        Assert.Equal(45, g.TotalQuantity);
    }

    [Fact]
    public void Single_Level_Trade_Is_Not_A_Sweep()
    {
        var s = new BigTradeSettings { Mode = ThresholdMode.Fixed, FixedBuyThreshold = 1, AbsoluteFloor = 1,
            Aggregation = AggregationMode.Sweep, SweepMinLevels = 3, SweepMinVolume = 1, TimeWindowMs = 500,
            MinSamples = 1 }.Validate();
        var d = new BigTradeDetector(s);
        d.OnTrade(Tr(0.0, 100, 200, AggressorSide.Buy), 99, 100, true);
        d.Flush();
        var g = Assert.Single(d.Snapshot(T.AddSeconds(2)));
        Assert.False(g.IsSweep);
    }

    // ── Determinism ──────────────────────────────────────────────────────

    [Fact]
    public void Replay_Produces_Identical_Group_Hash()
    {
        IReadOnlyList<BigTradeGroup> Run()
        {
            var d = new BigTradeDetector(Fixed(20, AggregationMode.AdjacentPrice) with { TimeWindowMs = 300 });
            for (int i = 0; i < 50; i++)
            {
                var side = i % 3 == 0 ? AggressorSide.Sell : AggressorSide.Buy;
                d.OnTrade(Tr(i * 0.05, 100 + (i % 5), 5 + (i % 7) * 4, side), 99, 101, true);
            }
            d.Flush();
            return d.Snapshot(T.AddSeconds(60));
        }

        Assert.Equal(BigTradeDetector.Hash(Run()), BigTradeDetector.Hash(Run()));
    }

    [Fact]
    public void Diagnostics_Track_Sides_And_Quality()
    {
        var d = new BigTradeDetector(Fixed(1));
        d.OnTrade(Tr(0, 101, 5, AggressorSide.Buy), 99, 101, true);
        d.OnTrade(Tr(1, 99, 5, AggressorSide.Sell), 99, 101, true);
        // Unknown via invalid book.
        var unknown = MarketEvent.Trade(1, RootSymbol.NQ, "NQZ5", "CME", T.AddSeconds(2), T.AddSeconds(2), 100, 5, AggressorSide.Unknown);
        d.OnTrade(unknown, 99, 101, bookValid: false);
        var diag = d.DiagnosticsSnapshot();

        Assert.Equal(3, diag.TradesProcessed);
        Assert.Equal(1, diag.BuyTrades);
        Assert.Equal(1, diag.SellTrades);
        Assert.Equal(1, diag.UnknownTrades);
        Assert.Equal(2, diag.NativeClassifications);
        Assert.Equal(1, diag.InvalidBookClassifications);
    }
}
