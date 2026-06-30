using FlowTerminal.Analytics.Footprints;
using FlowTerminal.Domain.Events;
using Xunit;

namespace FlowTerminal.Analytics.Tests;

/// <summary>
/// Manually-verified reference fixtures for the footprint aggregator: price-level
/// volumes/delta, deterministic POC + tie rule, diagonal imbalances with the
/// zero-denominator policy, stacked imbalances, zero prints, unfinished auctions,
/// large trades, order-independence (incremental == batch), and replay-hash stability.
/// </summary>
public class FootprintAggregatorTests
{
    // ask = aggressive buy (Buy aggressor), bid = aggressive sell (Sell aggressor).
    private static void Ask(Footprint f, long price, long qty) => f.AddAt(price, qty, AggressorSide.Buy);
    private static void Bid(Footprint f, long price, long qty) => f.AddAt(price, qty, AggressorSide.Sell);

    private static FootprintPriceLevel LevelAt(FootprintBar bar, long price)
    {
        foreach (var l in bar.Levels) if (l.PriceTicks == price) return l;
        throw new Xunit.Sdk.XunitException($"no level at {price}");
    }

    /// <summary>The documented worked example (bullish bar, range 99–105).</summary>
    private static Footprint ReferenceFixture()
    {
        var f = new Footprint();
        Ask(f, 100, 9); Bid(f, 100, 1);
        Ask(f, 101, 2); Bid(f, 101, 10);
        Ask(f, 102, 8); Bid(f, 102, 3);
        Ask(f, 103, 20); Bid(f, 103, 4);
        Ask(f, 104, 5);                   // bid 0 → interior zero print
        Bid(f, 105, 6);                   // ask 0 at the high
        Bid(f, 99, 7);                    // ask 0 at the low
        return f;
    }

    private static FootprintBar BuildReference(FootprintSettings? s = null) =>
        FootprintAggregator.Build(ReferenceFixture(), 100, 105, 99, 104,
            default, default, isClosed: true, s ?? FootprintSettings.Default);

    [Fact]
    public void Price_Levels_Bid_Ask_Total_And_Delta_Are_Correct()
    {
        var bar = BuildReference();
        var l103 = LevelAt(bar, 103);
        Assert.Equal(4, l103.BidVolume);
        Assert.Equal(20, l103.AskVolume);
        Assert.Equal(24, l103.TotalVolume);
        Assert.Equal(16, l103.Delta);

        Assert.Equal(44, bar.BuyVolume);
        Assert.Equal(31, bar.SellVolume);
        Assert.Equal(13, bar.Delta);
        Assert.Equal(75, bar.TotalVolume);
        Assert.Equal(16, bar.MaxPositiveLevelDelta);
        Assert.Equal(-8, bar.MaxNegativeLevelDelta); // level 101: ask2 − bid10
        Assert.Equal(11, bar.TradeCount);
    }

    [Fact]
    public void Poc_Is_Highest_Total_Volume_Level()
    {
        var bar = BuildReference();
        Assert.Equal(103, bar.PocTicks);
        Assert.True(LevelAt(bar, 103).IsPoc);
    }

    [Fact]
    public void Poc_Tie_Resolves_To_Level_Closest_To_Close()
    {
        // Two levels tie on total volume (10 each) at 100 and 104; close = 103 ⇒ 104 wins.
        var f = new Footprint();
        Ask(f, 100, 5); Bid(f, 100, 5);   // total 10
        Ask(f, 104, 6); Bid(f, 104, 4);   // total 10
        var bar = FootprintAggregator.Build(f, 100, 104, 100, 103, default, default, true);
        Assert.Equal(104, bar.PocTicks);
    }

    [Fact]
    public void Diagonal_Imbalances_Match_Reference()
    {
        var bar = BuildReference();
        Assert.True(LevelAt(bar, 103).IsBuyImbalance);   // ask20 vs bid@102=3 ⇒ 20 ≥ 9
        Assert.True(LevelAt(bar, 105).IsSellImbalance);  // bid6 vs ask@106=0 (zero-denominator)
        Assert.False(LevelAt(bar, 101).IsBuyImbalance);
        Assert.Equal(1, bar.BuyImbalanceCount);
        Assert.Equal(1, bar.SellImbalanceCount);
    }

    [Fact]
    public void Zero_Denominator_Policy_Ignore_Suppresses_The_Imbalance()
    {
        var bar = BuildReference(FootprintSettings.Default with { ZeroDenominator = ZeroDenominatorPolicy.Ignore });
        Assert.False(LevelAt(bar, 105).IsSellImbalance); // compared volume is zero ⇒ ignored
    }

    [Fact]
    public void Zero_Print_Is_Detected_Interior_But_Not_At_Extremes()
    {
        var bar = BuildReference();
        Assert.True(LevelAt(bar, 104).IsZeroPrint);   // bid 0, ask 5, interior
        Assert.False(LevelAt(bar, 105).IsZeroPrint);  // ask 0 but at the high → excluded
        Assert.False(LevelAt(bar, 99).IsZeroPrint);   // ask 0 but at the low → excluded
    }

    [Fact]
    public void Unfinished_Auction_At_High_Is_Confirmed_When_Bar_Closed()
    {
        var bar = BuildReference();
        Assert.Equal(AuctionState.Confirmed, bar.HighUnfinished); // bid present at high (105)
        Assert.Equal(AuctionState.None, bar.LowUnfinished);       // ask 0 at low

        var dev = FootprintAggregator.Build(ReferenceFixture(), 100, 105, 99, 104, default, default, isClosed: false);
        Assert.Equal(AuctionState.Developing, dev.HighUnfinished);
    }

    [Fact]
    public void Stacked_Imbalance_Detects_Three_Consecutive_Levels()
    {
        var f = new Footprint();
        Ask(f, 100, 1); Bid(f, 100, 1);
        Ask(f, 101, 10); Bid(f, 101, 1);
        Ask(f, 102, 10); Bid(f, 102, 1);
        Ask(f, 103, 10); Bid(f, 103, 1);
        var bar = FootprintAggregator.Build(f, 100, 103, 100, 103, default, default, true);

        // 100 also qualifies (ask1 over an empty 99 under RequireMinNumerator), so the
        // stacked run spans 100–103 (4 levels).
        Assert.Contains(bar.StackedZones, z => z.Side == ImbalanceSide.Buy && z.LevelCount >= 3
            && z.LowTicks == 100 && z.HighTicks == 103);
        Assert.True(LevelAt(bar, 102).IsStackedBuyImbalance);
    }

    [Fact]
    public void Large_Trade_Flagged_When_Single_Print_Meets_Threshold()
    {
        var f = new Footprint();
        Ask(f, 100, 60); // single 60-lot print
        Bid(f, 100, 5);
        var bar = FootprintAggregator.Build(f, 100, 100, 100, 100, default, default, true,
            FootprintSettings.Default with { LargeTradeThreshold = 50 });
        Assert.True(LevelAt(bar, 100).LargeTradeCount > 0);
        Assert.Equal(60, LevelAt(bar, 100).LargestBuyTrade);
        Assert.Equal(60, LevelAt(bar, 100).MaxTradeSize);
    }

    [Fact]
    public void Aggregation_Is_Order_Independent_And_Hash_Is_Stable()
    {
        var a = BuildReference();

        // Same trades, reverse-ish feed order → identical bar content and hash.
        var f = new Footprint();
        Bid(f, 99, 7); Bid(f, 105, 6); Ask(f, 104, 5);
        Ask(f, 103, 20); Bid(f, 103, 4);
        Ask(f, 102, 8); Bid(f, 102, 3);
        Ask(f, 101, 2); Bid(f, 101, 10);
        Ask(f, 100, 9); Bid(f, 100, 1);
        var b = FootprintAggregator.Build(f, 100, 105, 99, 104, default, default, true);

        Assert.Equal(a.Hash, b.Hash);
        Assert.Equal(a.PocTicks, b.PocTicks);
        Assert.Equal(a.Delta, b.Delta);
    }

    [Fact]
    public void Delta_Percent_Is_Safe_When_Total_Is_Zero()
    {
        var l = new FootprintPriceLevel(100, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            false, false, false, false, false, false, false, 0);
        Assert.Equal(0.0, l.DeltaPercent);
        Assert.Equal(0, l.TotalVolume);
    }

    [Fact]
    public void Empty_Footprint_Produces_Safe_Bar()
    {
        var bar = FootprintAggregator.Build(new Footprint(), 0, 0, 0, 0, default, default, true);
        Assert.Empty(bar.Levels);
        Assert.Equal(long.MinValue, bar.PocTicks);
        Assert.Equal(0, bar.TotalVolume);
    }
}
