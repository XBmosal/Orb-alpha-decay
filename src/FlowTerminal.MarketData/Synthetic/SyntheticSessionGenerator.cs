using FlowTerminal.Domain.Events;
using FlowTerminal.Domain.Instruments;

namespace FlowTerminal.MarketData.Synthetic;

public sealed record SyntheticOptions
{
    /// <summary>Seed driving the deterministic stream. Same seed → identical events.</summary>
    public ulong Seed { get; init; } = 1;

    /// <summary>Starting mid price in exchange points.</summary>
    public decimal StartPrice { get; init; } = 20_000m;

    /// <summary>Average milliseconds between simulation steps (drives event pacing).</summary>
    public int MeanInterEventMs { get; init; } = 5;

    /// <summary>Probability (0..1) that a given aggressive clip is an outsized sweep order.</summary>
    public double LargeTradeProbability { get; init; } = 0.01;

    /// <summary>Inject a deliberate sequence gap after this many events (0 = never). For tests.</summary>
    public int InjectGapAfter { get; init; }
}

/// <summary>
/// Public, replay-stable façade over the stateful synthetic market engine
/// (<see cref="SyntheticEventGenerator"/>). It preserves the original pull interface
/// (<see cref="Next"/> / <see cref="Generate"/>) so the mock provider, the historical
/// provider and warm-up replay drive it unchanged, while every event now comes from a
/// genuine, incrementally-mutated limit-order book rather than a memoryless touch
/// quote. Output is a pure function of the seed and is flagged
/// <see cref="MarketEventFlags.Synthetic"/> on every event; it must never be presented
/// as real exchange data.
/// </summary>
public sealed class SyntheticSessionGenerator
{
    private readonly SyntheticEventGenerator _engine;

    public SyntheticSessionGenerator(int instrumentId, Contract contract, DateTime startUtc, SyntheticOptions options)
        => _engine = new SyntheticEventGenerator(instrumentId, contract, startUtc, options);

    /// <summary>The underlying stateful engine (book + regime + diagnostics) for tests/diagnostics.</summary>
    public SyntheticEventGenerator Engine => _engine;

    /// <summary>Produces the next canonical event. Deterministic given construction parameters.</summary>
    public MarketEvent Next() => _engine.Next();

    /// <summary>Lazily produces up to <paramref name="count"/> canonical events (deterministic).</summary>
    public IEnumerable<MarketEvent> Generate(int count) => _engine.Generate(count);
}
