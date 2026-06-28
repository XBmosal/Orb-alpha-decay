using FlowTerminal.Domain.Events;
using FlowTerminal.Domain.Instruments;
using FlowTerminal.MarketData.Synthetic;
using FlowTerminal.OrderBook;
using Xunit;

namespace FlowTerminal.OrderBook.Tests;

/// <summary>
/// Proves the core replay-seeking guarantee: restoring a checkpoint and applying
/// only the subsequent events yields a book state identical to replaying the whole
/// stream from the start. This is what lets replay jump to a time efficiently.
/// </summary>
public class CheckpointSeekTests
{
    private static readonly DateTime Ts = new(2024, 6, 3, 14, 0, 0, DateTimeKind.Utc);

    // A deterministic depth stream with adds and removals on both sides.
    private static List<MarketEvent> DepthStream(int count)
    {
        var rng = new DeterministicRng(2024);
        var events = new List<MarketEvent>(count + 2)
        {
            MarketEvent.Control(1, RootSymbol.NQ, "NQZ5", "CME Globex", MarketEventType.SnapshotStart, Ts, Ts),
        };

        // Seed a non-crossed book.
        events.Add(Depth(Side.Bid, 1000, 10));
        events.Add(Depth(Side.Ask, 1004, 10));
        events.Add(MarketEvent.Control(1, RootSymbol.NQ, "NQZ5", "CME Globex", MarketEventType.SnapshotEnd, Ts, Ts));

        long bid = 1000, ask = 1004;
        for (int i = 0; i < count; i++)
        {
            if (rng.NextDouble() < 0.5)
            {
                bid = Math.Min(ask - 1, bid + (rng.NextInt(3) - 1));
                events.Add(Depth(Side.Bid, bid, 1 + rng.NextInt(50)));
            }
            else
            {
                ask = Math.Max(bid + 1, ask + (rng.NextInt(3) - 1));
                events.Add(Depth(Side.Ask, ask, 1 + rng.NextInt(50)));
            }
        }

        return events;
    }

    private static MarketEvent Depth(Side side, long price, long size)
    {
        var type = side == Side.Bid ? MarketEventType.BidUpdate : MarketEventType.AskUpdate;
        return MarketEvent.Quote(1, RootSymbol.NQ, "NQZ5", "CME Globex", type, Ts, Ts, side, price, size);
    }

    private static (long bestBid, long bestAsk, long cumBid, long cumAsk, bool valid) State(MarketByPriceOrderBook b)
        => (b.BestBidTicks, b.BestAskTicks, b.CumulativeDepth(Side.Bid, 50), b.CumulativeDepth(Side.Ask, 50), b.IsValid);

    [Fact]
    public void Checkpoint_Restore_Then_Continue_Matches_Full_Replay()
    {
        var events = DepthStream(2000);
        int half = events.Count / 2;

        // Full replay.
        var full = new MarketByPriceOrderBook();
        foreach (var e in events)
        {
            full.Apply(e);
        }

        // Replay first half, checkpoint, restore into a fresh book, apply the rest.
        var first = new MarketByPriceOrderBook();
        for (int i = 0; i < half; i++)
        {
            first.Apply(events[i]);
        }

        var checkpoint = first.CreateCheckpoint();
        var seeked = new MarketByPriceOrderBook();
        seeked.RestoreCheckpoint(checkpoint);
        for (int i = half; i < events.Count; i++)
        {
            seeked.Apply(events[i]);
        }

        Assert.Equal(State(full), State(seeked));
    }
}
