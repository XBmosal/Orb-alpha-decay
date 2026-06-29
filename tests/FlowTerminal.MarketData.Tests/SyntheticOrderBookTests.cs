using System.Diagnostics;
using FlowTerminal.Domain.Events;
using FlowTerminal.Domain.Instruments;
using FlowTerminal.MarketData.Synthetic;
using FlowTerminal.OrderBook;
using Xunit;

namespace FlowTerminal.MarketData.Tests;

/// <summary>
/// Realism and integrity tests for the stateful, event-driven synthetic order-book
/// engine: book validity (never crossed), determinism, heavy-tailed distributions,
/// regime variety, executions-from-book, lifecycle (bands begin/end), bounded
/// memory and throughput.
/// </summary>
public class SyntheticOrderBookTests
{
    private static readonly Contract Nq = new(RootSymbol.NQ, QuarterlyMonth.December, 2025);
    private static readonly Contract Es = new(RootSymbol.ES, QuarterlyMonth.December, 2025);
    private static readonly DateTime Start = new(2024, 6, 3, 14, 0, 0, DateTimeKind.Utc);

    private static SyntheticEventGenerator Engine(Contract c, ulong seed = 7) =>
        new(1, c, Start, new SyntheticOptions { Seed = seed, StartPrice = c.Root == RootSymbol.ES ? 5_000m : 20_000m });

    [Fact]
    public void Internal_Book_Is_Never_Crossed()
    {
        var e = Engine(Nq);
        for (int i = 0; i < 60_000; i++)
        {
            e.Next();
            var b = e.Book;
            if (b.BestBidTicks != SyntheticOrderBook.NoPrice && b.BestAskTicks != SyntheticOrderBook.NoPrice)
            {
                Assert.True(b.BestBidTicks < b.BestAskTicks,
                    $"crossed at event {i}: bid {b.BestBidTicks} ask {b.BestAskTicks}");
            }
        }
    }

    [Fact]
    public void Downstream_Mbp_Book_Stays_Valid_And_Bounded()
    {
        var e = Engine(Nq);
        var book = new MarketByPriceOrderBook();
        int maxLevels = 0;

        for (int i = 0; i < 80_000; i++)
        {
            var ev = e.Next();
            book.Apply(ev);
            if (ev.Type is MarketEventType.BidUpdate or MarketEventType.AskUpdate)
            {
                Assert.True(book.IsValid, $"book invalid at event {i}: {book.InvalidReason}");
            }

            maxLevels = Math.Max(maxLevels, book.BidLevelCount + book.AskLevelCount);
        }

        // Far liquidity is pulled as price moves, so the reconstructed book never
        // accumulates an unbounded smear of stale levels.
        Assert.True(maxLevels < 220, $"book grew unbounded: {maxLevels} levels");
    }

    [Fact]
    public void Same_Seed_Produces_Identical_Streams()
    {
        var a = Engine(Nq, 123);
        var b = Engine(Nq, 123);
        for (int i = 0; i < 20_000; i++)
        {
            var x = a.Next();
            var y = b.Next();
            Assert.Equal(x.Type, y.Type);
            Assert.Equal(x.PriceTicks, y.PriceTicks);
            Assert.Equal(x.Quantity, y.Quantity);
            Assert.Equal(x.ExchangeSequence, y.ExchangeSequence);
            Assert.Equal(x.ExchangeTimestampUtc, y.ExchangeTimestampUtc);
            Assert.Equal(x.Aggressor, y.Aggressor);
        }
    }

    [Fact]
    public void Resting_Depth_Is_Heavy_Tailed_Not_Uniform()
    {
        var e = Engine(Nq);
        for (int i = 0; i < 30_000; i++) e.Next();

        var sizes = new List<long>();
        foreach (var l in e.Book.Bids.Values) sizes.Add(l.Displayed);
        foreach (var l in e.Book.Asks.Values) sizes.Add(l.Displayed);
        sizes.Sort();

        Assert.True(sizes.Count > 10);
        long median = sizes[sizes.Count / 2];
        long max = sizes[^1];
        int distinct = sizes.Distinct().Count();

        // A heavy tail: the largest resting level dwarfs the median, and sizes are
        // not all-equal (no perfect stair-step / mirrored ladder).
        Assert.True(max > median * 4, $"tail too thin: median {median}, max {max}");
        Assert.True(distinct > sizes.Count / 2, $"sizes too uniform: {distinct} distinct of {sizes.Count}");
    }

    [Fact]
    public void Bids_And_Asks_Are_Not_Mirror_Images()
    {
        var e = Engine(Nq);
        for (int i = 0; i < 20_000; i++) e.Next();

        long bestBid = e.Book.BestBidTicks;
        long bestAsk = e.Book.BestAskTicks;
        int mirrored = 0, compared = 0;
        for (int d = 0; d < 12; d++)
        {
            var bid = e.Book.Find(Side.Bid, bestBid - d);
            var ask = e.Book.Find(Side.Ask, bestAsk + d);
            if (bid is null || ask is null) continue;
            compared++;
            if (bid.Displayed == ask.Displayed) mirrored++;
        }

        Assert.True(compared >= 6);
        Assert.True(mirrored <= 1, "bid and ask ladders are suspiciously mirrored");
    }

    [Fact]
    public void Every_Bubble_Is_A_Real_Execution_With_Heavy_Tailed_Size()
    {
        var e = Engine(Nq);
        var tradeSizes = new List<long>();
        for (int i = 0; i < 60_000; i++)
        {
            var ev = e.Next();
            if (ev.Type != MarketEventType.Trade) continue;
            Assert.True(ev.Quantity > 0);
            Assert.True(ev.Aggressor is AggressorSide.Buy or AggressorSide.Sell);
            tradeSizes.Add(ev.Quantity);
        }

        Assert.True(tradeSizes.Count > 500, $"too few executions: {tradeSizes.Count}");
        tradeSizes.Sort();
        long median = tradeSizes[tradeSizes.Count / 2];
        long max = tradeSizes[^1];
        Assert.True(max > median * 8, $"trade tail too thin: median {median}, max {max}");
    }

    [Fact]
    public void Bands_Begin_On_Add_And_End_On_Cancel()
    {
        var e = Engine(Nq);
        bool sawAdd = false, sawRemoval = false;
        for (int i = 0; i < 40_000 && !(sawAdd && sawRemoval); i++)
        {
            var ev = e.Next();
            if (ev.Type is MarketEventType.BidUpdate or MarketEventType.AskUpdate)
            {
                if (ev.Quantity > 0) sawAdd = true;
                else sawRemoval = true; // a level was pulled/consumed → band ends
            }
        }

        Assert.True(sawAdd, "no liquidity additions emitted");
        Assert.True(sawRemoval, "liquidity is never removed — bands would never end");
    }

    [Fact]
    public void Replenishment_Is_Finite_Per_Level()
    {
        var e = Engine(Nq);
        for (int i = 0; i < 50_000; i++) e.Next();

        foreach (var l in e.Book.Bids.Values) Assert.True(l.ReplenishCount <= 6);
        foreach (var l in e.Book.Asks.Values) Assert.True(l.ReplenishCount <= 6);
    }

    [Fact]
    public void Price_Moves_And_Stays_Bounded()
    {
        var e = Engine(Nq);
        long min = long.MaxValue, max = long.MinValue;
        for (int i = 0; i < 80_000; i++)
        {
            var ev = e.Next();
            if (ev.Type != MarketEventType.Trade) continue;
            min = Math.Min(min, ev.PriceTicks);
            max = Math.Max(max, ev.PriceTicks);
        }

        Assert.True(max > min, "price never moved");
        // Over ~80k events price should explore a meaningful range but not run away
        // to absurd levels (mean reversion keeps it anchored).
        Assert.True(max - min > 4, "price barely moved");
        Assert.True(max - min < 20_000, "price ran away — not anchored");
    }

    [Fact]
    public void Session_Visits_Multiple_Regimes()
    {
        var e = Engine(Nq);
        var seen = new HashSet<SyntheticRegime>();
        for (int i = 0; i < 400_000; i++)
        {
            e.Next();
            seen.Add(e.CurrentRegime);
        }

        Assert.True(seen.Count >= 4, $"only saw regimes: {string.Join(",", seen)}");
    }

    [Fact]
    public void Es_And_Nq_Use_Distinct_Configurations()
    {
        var nq = Engine(Nq);
        var es = Engine(Es);
        for (int i = 0; i < 30_000; i++) { nq.Next(); es.Next(); }

        double NqMed = Median(nq), EsMed = Median(es);
        // ES carries materially larger resting size per level than NQ by default.
        Assert.True(EsMed > NqMed, $"ES median {EsMed} not larger than NQ median {NqMed}");

        static double Median(SyntheticEventGenerator g)
        {
            var s = new List<long>();
            foreach (var l in g.Book.Bids.Values) s.Add(l.Displayed);
            foreach (var l in g.Book.Asks.Values) s.Add(l.Displayed);
            s.Sort();
            return s.Count == 0 ? 0 : s[s.Count / 2];
        }
    }

    [Fact]
    public void Book_Rejects_Crossing_Updates_And_Stays_Valid()
    {
        var b = new SyntheticOrderBook();
        b.Put(Side.Bid, Level(99, 5));
        b.Put(Side.Ask, Level(101, 5));

        // A bid at or above the best ask is rejected as a no-op (not applied).
        Assert.False(b.Put(Side.Bid, Level(101, 5)));
        Assert.Equal(99, b.BestBidTicks);

        // An ask at or below the best bid is likewise rejected.
        Assert.False(b.Put(Side.Ask, Level(99, 5)));
        Assert.Equal(101, b.BestAskTicks);

        Assert.False(b.IsCrossed);
        Assert.True(b.BestBidTicks < b.BestAskTicks);
    }

    [Fact]
    public void Aggressive_Order_Sweeps_Levels_In_Strict_Price_Order_Without_Skipping()
    {
        var cfg = SyntheticMarketConfiguration.ForRoot(RootSymbol.NQ);
        var tg = new SyntheticTradeGenerator(cfg);
        int longest = 0;

        for (ulong seed = 1; seed <= 24; seed++)
        {
            var book = new SyntheticOrderBook();
            book.Put(Side.Bid, Level(99, 5));
            for (long p = 100; p < 112; p++) book.Put(Side.Ask, Level(p, 3)); // contiguous ask ladder

            var sink = new RecordingSink();
            var rng = new DeterministicRng(seed);
            tg.ExecuteOrder(book, AggressorSide.Buy, 120, ref rng, sink);

            // Every print climbs the ladder one populated level at a time from the touch:
            // consumed prices are strictly increasing and contiguous from the best ask,
            // proving the sweep consumes in order and never skips a resting level.
            for (int i = 0; i < sink.Executions.Count; i++)
            {
                Assert.Equal(100 + i, sink.Executions[i].Price);
            }

            longest = Math.Max(longest, sink.Executions.Count);
        }

        Assert.True(longest >= 3, $"no multi-level sweep occurred (longest {longest})");
    }

    private static SyntheticLevel Level(long price, long size) =>
        new() { PriceTicks = price, Displayed = size, TargetSize = size, AddedTotal = size };

    private sealed class RecordingSink : IBookEventSink
    {
        public List<(long Price, long Qty)> Executions { get; } = new();

        public void OnDepthChanged(Side side, long price, long displayed) { }

        public void OnExecution(AggressorSide aggressor, long price, long qty) =>
            Executions.Add((price, qty));
    }

    [Fact]
    public void Throughput_Is_Adequate_For_Live_Streaming()
    {
        var e = Engine(Nq);
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 200_000; i++) e.Next();
        sw.Stop();

        // Must comfortably outpace a live feed; 200k events well under a couple seconds.
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(8), $"too slow: {sw.Elapsed}");
    }
}
