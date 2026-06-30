using BenchmarkDotNet.Attributes;
using FlowTerminal.Domain.Events;
using FlowTerminal.Domain.Instruments;
using FlowTerminal.MarketData.Synthetic;
using FlowTerminal.OrderBook;

namespace FlowTerminal.Benchmarks;

/// <summary>
/// Measures market-by-price book reconstruction throughput (events applied/sec)
/// and allocation behavior. The book must keep up with the canonical event rate
/// without per-event heap churn.
/// </summary>
[MemoryDiagnoser]
public class OrderBookBenchmark
{
    private MarketEvent[] _events = Array.Empty<MarketEvent>();

    [Params(200_000)]
    public int EventCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var ts = new DateTime(2024, 6, 3, 14, 0, 0, DateTimeKind.Utc);
        var rng = new DeterministicRng(7);
        var list = new List<MarketEvent>(EventCount + 3)
        {
            MarketEvent.Control(1, RootSymbol.NQ, "NQZ5", "CME Globex", MarketEventType.SnapshotStart, ts, ts),
            Depth(Side.Bid, 1000, 10, ts),
            Depth(Side.Ask, 1004, 10, ts),
            MarketEvent.Control(1, RootSymbol.NQ, "NQZ5", "CME Globex", MarketEventType.SnapshotEnd, ts, ts),
        };

        long bid = 1000, ask = 1004;
        for (int i = 0; i < EventCount; i++)
        {
            if (rng.NextDouble() < 0.5)
            {
                bid = Math.Min(ask - 1, bid + (rng.NextInt(3) - 1));
                list.Add(Depth(Side.Bid, bid, 1 + rng.NextInt(50), ts));
            }
            else
            {
                ask = Math.Max(bid + 1, ask + (rng.NextInt(3) - 1));
                list.Add(Depth(Side.Ask, ask, 1 + rng.NextInt(50), ts));
            }
        }

        _events = list.ToArray();
    }

    private static MarketEvent Depth(Side side, long price, long size, DateTime ts)
    {
        var type = side == Side.Bid ? MarketEventType.BidUpdate : MarketEventType.AskUpdate;
        return MarketEvent.Quote(1, RootSymbol.NQ, "NQZ5", "CME Globex", type, ts, ts, side, price, size);
    }

    [Benchmark]
    public long ApplyAll()
    {
        var book = new MarketByPriceOrderBook();
        foreach (var e in _events)
        {
            book.Apply(e);
        }

        return book.BestBidTicks;
    }
}
