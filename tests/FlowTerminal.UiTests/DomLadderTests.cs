using FlowTerminal.Analytics.Profiles;
using FlowTerminal.Charting.Dom;
using FlowTerminal.Domain.Events;
using FlowTerminal.Domain.Instruments;
using FlowTerminal.OrderBook;
using Xunit;

namespace FlowTerminal.UiTests;

/// <summary>
/// Correctness, reconciliation and preset tests for the read-only DOM ladder:
/// touch-outward cumulative depth, best bid/ask + distance, executed-volume
/// reconciliation with the shared profile, pulling/stacking, wall detection, and the
/// column/preset model.
/// </summary>
public class DomLadderTests
{
    private static readonly DateTime T = new(2024, 6, 3, 14, 0, 0, DateTimeKind.Utc);

    private static MarketEvent Quote(MarketEventType type, Side side, long price, long qty, long seq) =>
        MarketEvent.Quote(1, RootSymbol.NQ, "NQZ5", "CME Globex", type, T, T, side, price, qty,
            exchangeSequence: seq, flags: MarketEventFlags.HasExchangeSequence);

    private static MarketByPriceOrderBook Book((long P, long Q)[] bids, (long P, long Q)[] asks)
    {
        var book = new MarketByPriceOrderBook();
        long seq = 0;
        foreach (var (p, q) in asks) book.Apply(Quote(MarketEventType.AskUpdate, Side.Ask, p, q, ++seq));
        foreach (var (p, q) in bids) book.Apply(Quote(MarketEventType.BidUpdate, Side.Bid, p, q, ++seq));
        return book;
    }

    private static DomRow At(IReadOnlyList<DomRow> rows, long price)
    {
        foreach (var r in rows) if (r.PriceTicks == price) return r;
        throw new Xunit.Sdk.XunitException($"no DOM row at {price}");
    }

    private static IReadOnlyList<DomRow> Standard(PullStackTracker? tracker = null, long wallFloor = long.MaxValue)
    {
        var book = Book(
            bids: new[] { (100L, 5L), (99L, 10L), (98L, 3L) },
            asks: new[] { (101L, 4L), (102L, 8L), (103L, 2L) });
        return ReadOnlyDom.Build(book, new VolumeProfile(), 4, tracker, wallFloor);
    }

    [Fact]
    public void Cumulative_Depth_Accumulates_From_The_Touch_Outward()
    {
        var rows = Standard();
        // Ask cumulative: 101→4, 102→12, 103→14 (touch is smallest, grows outward).
        Assert.Equal(4, At(rows, 101).CumulativeAsk);
        Assert.Equal(12, At(rows, 102).CumulativeAsk);
        Assert.Equal(14, At(rows, 103).CumulativeAsk);
        // Bid cumulative: 100→5, 99→15, 98→18.
        Assert.Equal(5, At(rows, 100).CumulativeBid);
        Assert.Equal(15, At(rows, 99).CumulativeBid);
        Assert.Equal(18, At(rows, 98).CumulativeBid);
    }

    [Fact]
    public void Best_Bid_Ask_Flags_And_Distance_Are_Correct()
    {
        var rows = Standard();
        Assert.True(At(rows, 100).IsBestBid);
        Assert.True(At(rows, 101).IsBestAsk);
        Assert.False(At(rows, 99).IsBestBid);

        Assert.Equal(0, At(rows, 101).DistanceTicks);
        Assert.Equal(2, At(rows, 103).DistanceTicks);
        Assert.Equal(0, At(rows, 100).DistanceTicks);
        Assert.Equal(2, At(rows, 98).DistanceTicks);
    }

    [Fact]
    public void Rows_Are_Tick_Aligned_Descending_And_Never_Negative()
    {
        var rows = Standard();
        for (int i = 1; i < rows.Count; i++)
            Assert.Equal(rows[i - 1].PriceTicks - 1, rows[i].PriceTicks); // exact ticks, descending
        foreach (var r in rows)
        {
            Assert.True(r.BidSize >= 0 && r.AskSize >= 0);
            Assert.True(r.CumulativeBid >= 0 && r.CumulativeAsk >= 0);
        }
    }

    [Fact]
    public void Dom_Best_Bid_Ask_Reconcile_With_The_Canonical_Book()
    {
        var book = Book(new[] { (100L, 5L) }, new[] { (101L, 4L) });
        var rows = ReadOnlyDom.Build(book, new VolumeProfile(), 4);
        Assert.Equal(book.BestBidTicks, At(rows, 100).PriceTicks);
        Assert.True(At(rows, 100).IsBestBid && At(rows, 101).IsBestAsk);
        Assert.True(book.BestBidTicks < book.BestAskTicks); // never crossed
    }

    [Fact]
    public void Executed_Volume_Reconciles_With_The_Shared_Profile()
    {
        var book = Book(new[] { (100L, 5L) }, new[] { (101L, 4L) });
        var profile = new VolumeProfile();
        profile.AddAt(100, 7, AggressorSide.Buy);   // traded at ask @100
        profile.AddAt(100, 3, AggressorSide.Sell);  // traded at bid @100
        var rows = ReadOnlyDom.Build(book, profile, 4);

        Assert.Equal(profile.BuyVolumeAt(100), At(rows, 100).TradedAtAsk);
        Assert.Equal(profile.SellVolumeAt(100), At(rows, 100).TradedAtBid);
        Assert.Equal(7 - 3, At(rows, 100).Delta);
        Assert.Equal(10, At(rows, 100).TotalTraded);
    }

    [Fact]
    public void Empty_Book_Produces_No_Rows()
    {
        var rows = ReadOnlyDom.Build(new MarketByPriceOrderBook(), new VolumeProfile(), 4);
        Assert.Empty(rows);
    }

    [Fact]
    public void Pulling_And_Stacking_Are_Tracked_Per_Level()
    {
        var tracker = new PullStackTracker();
        tracker.OnDepth(Quote(MarketEventType.BidUpdate, Side.Bid, 99, 10, 1)); // baseline
        tracker.OnDepth(Quote(MarketEventType.BidUpdate, Side.Bid, 99, 4, 2));  // pulled 6
        tracker.OnDepth(Quote(MarketEventType.AskUpdate, Side.Ask, 102, 8, 3)); // baseline
        tracker.OnDepth(Quote(MarketEventType.AskUpdate, Side.Ask, 102, 13, 4)); // stacked 5

        var rows = Standard(tracker);
        Assert.Equal(6, At(rows, 99).BidPulled);
        Assert.Equal(5, At(rows, 102).AskStacked);
    }

    [Fact]
    public void Wall_Is_Flagged_On_Outsized_Levels_Only()
    {
        // ask 80 @102 is far larger than the visible ask median (4) → wall; touch (4) is not.
        var book = Book(new[] { (100L, 5L) }, new[] { (101L, 4L), (102L, 80L), (103L, 2L) });
        var rows = ReadOnlyDom.Build(book, new VolumeProfile(), 4, tracker: null, wallFloor: 50);
        Assert.True(At(rows, 102).IsAskWall);
        Assert.False(At(rows, 101).IsAskWall);
    }

    // ── Column / preset model ───────────────────────────────────────────────

    [Fact]
    public void Every_Column_Type_Has_A_Descriptor()
    {
        foreach (DomColumnType t in Enum.GetValues<DomColumnType>())
            Assert.Equal(t, DomColumnRegistry.For(t).Type);
    }

    [Fact]
    public void Built_In_Presets_Reference_Known_Columns_And_Flag_Mbo()
    {
        Assert.Equal(10, DomPresetRegistry.BuiltIns.Count);
        foreach (var preset in DomPresetRegistry.BuiltIns)
        {
            Assert.True(preset.IsBuiltIn);
            Assert.NotEmpty(preset.Columns);
            foreach (var c in preset.Columns) _ = DomColumnRegistry.For(c); // throws if unknown
        }

        Assert.True(DomPresetRegistry.ByName("MBO Analytics")!.RequiresMbo);
        Assert.False(DomPresetRegistry.ByName("Classic Depth")!.RequiresMbo);
        Assert.Equal("Classic Depth", DomPresetRegistry.Default.Name);
    }

    [Fact]
    public void Hash_Is_Deterministic_For_Identical_Snapshots()
    {
        var a = Standard();
        var b = Standard();
        Assert.Equal(ReadOnlyDom.Hash(a), ReadOnlyDom.Hash(b));
        Assert.NotEqual(0UL, ReadOnlyDom.Hash(a));
    }

    [Fact]
    public void Hash_Changes_When_Rows_Differ_Or_Reorder()
    {
        var rows = Standard();
        ulong baseline = ReadOnlyDom.Hash(rows);

        // Reorder: hashing is order-sensitive.
        var reversed = rows.Reverse().ToList();
        Assert.NotEqual(baseline, ReadOnlyDom.Hash(reversed));

        // A changed size flips the hash.
        var mutated = rows.ToList();
        mutated[0] = mutated[0] with { BidSize = mutated[0].BidSize + 1 };
        Assert.NotEqual(baseline, ReadOnlyDom.Hash(mutated));

        // Empty snapshot is stable and distinct.
        Assert.Equal(ReadOnlyDom.Hash(Array.Empty<DomRow>()), ReadOnlyDom.Hash(new List<DomRow>()));
    }
}
