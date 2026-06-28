using FlowTerminal.Domain.Events;
using FlowTerminal.Domain.Instruments;

namespace FlowTerminal.MarketData.Synthetic;

public sealed record SyntheticOptions
{
    /// <summary>Seed driving the deterministic stream. Same seed → identical events.</summary>
    public ulong Seed { get; init; } = 1;

    /// <summary>Starting mid price in exchange points.</summary>
    public decimal StartPrice { get; init; } = 20_000m;

    /// <summary>Average milliseconds between events.</summary>
    public int MeanInterEventMs { get; init; } = 5;

    /// <summary>Probability (0..1) that a given trade is an outsized "large" trade.</summary>
    public double LargeTradeProbability { get; init; } = 0.01;

    /// <summary>Inject a deliberate sequence gap after this many events (0 = never). For tests.</summary>
    public int InjectGapAfter { get; init; }
}

/// <summary>
/// Generates a deterministic, replayable stream of canonical NQ/ES-like events:
/// a tick-quantized random walk producing trades, best bid/ask updates, and
/// market-by-price depth changes, with occasional large trades. Entirely a
/// function of the seed and event index, so it is reproducible and seekable.
///
/// This is SIMULATED data and is flagged <see cref="MarketEventFlags.Synthetic"/>
/// on every event. It must never be presented as real exchange data.
/// </summary>
public sealed class SyntheticSessionGenerator
{
    private readonly InstrumentSpec _spec;
    private readonly Contract _contract;
    private readonly SyntheticOptions _options;
    private readonly int _instrumentId;
    private readonly long _spreadTicks = 1;

    private DeterministicRng _rng;
    private long _midTicks;
    private long _sequence;
    private DateTime _clockUtc;
    private long _emitted;

    public SyntheticSessionGenerator(int instrumentId, Contract contract, DateTime startUtc, SyntheticOptions options)
    {
        if (startUtc.Kind == DateTimeKind.Local)
        {
            throw new ArgumentException("startUtc must be UTC.", nameof(startUtc));
        }

        _instrumentId = instrumentId;
        _contract = contract;
        _spec = contract.Spec;
        _options = options;
        _rng = new DeterministicRng(options.Seed);
        _midTicks = PriceConverter.ToTicks(_spec, options.StartPrice);
        _clockUtc = DateTime.SpecifyKind(startUtc, DateTimeKind.Utc);
        _sequence = 0;
    }

    /// <summary>Lazily produces up to <paramref name="count"/> canonical events (deterministic).</summary>
    public IEnumerable<MarketEvent> Generate(int count)
    {
        for (int i = 0; i < count; i++)
        {
            yield return Next();
        }
    }

    /// <summary>Produces the next event. Deterministic given construction parameters.</summary>
    public MarketEvent Next()
    {
        // Advance the clock by a deterministic positive interval.
        int dtMs = 1 + _rng.NextInt(Math.Max(1, _options.MeanInterEventMs * 2));
        _clockUtc = _clockUtc.AddMilliseconds(dtMs);

        // Sequence numbering, with optional deliberate gap injection for tests.
        _sequence++;
        if (_options.InjectGapAfter > 0 && _emitted == _options.InjectGapAfter)
        {
            _sequence += 5; // skip 5 sequence numbers to simulate a feed gap
        }

        _emitted++;

        // Random walk of the mid in ticks: -1, 0, or +1 with mild mean reversion.
        int step = _rng.NextInt(3) - 1;
        _midTicks = Math.Max(1, _midTicks + step);

        long bestBid = _midTicks - _spreadTicks;
        long bestAsk = _midTicks + _spreadTicks;

        // Decide event type: ~55% trade, ~25% bid update, ~20% ask update.
        int roll = _rng.NextInt(100);
        const MarketEventFlags baseFlags =
            MarketEventFlags.Synthetic | MarketEventFlags.HasExchangeSequence | MarketEventFlags.HasExchangeTimestamp;

        if (roll < 55)
        {
            bool buyAggressor = _rng.NextDouble() < 0.5;
            long priceTicks = buyAggressor ? bestAsk : bestBid;
            long qty = NextTradeQuantity();
            return MarketEvent.Trade(
                _instrumentId, _contract.Root, _contract.Symbol, _spec.Exchange,
                _clockUtc, _clockUtc, priceTicks, qty,
                buyAggressor ? AggressorSide.Buy : AggressorSide.Sell,
                exchangeSequence: _sequence,
                tradeId: _sequence,
                flags: baseFlags | MarketEventFlags.AggressorSupplied);
        }

        if (roll < 80)
        {
            long qty = 1 + _rng.NextInt(250);
            return MarketEvent.Quote(
                _instrumentId, _contract.Root, _contract.Symbol, _spec.Exchange,
                MarketEventType.BidUpdate, _clockUtc, _clockUtc, Side.Bid, bestBid, qty,
                exchangeSequence: _sequence, flags: baseFlags);
        }

        long askQty = 1 + _rng.NextInt(250);
        return MarketEvent.Quote(
            _instrumentId, _contract.Root, _contract.Symbol, _spec.Exchange,
            MarketEventType.AskUpdate, _clockUtc, _clockUtc, Side.Ask, bestAsk, askQty,
            exchangeSequence: _sequence, flags: baseFlags);
    }

    private long NextTradeQuantity()
    {
        if (_rng.NextDouble() < _options.LargeTradeProbability)
        {
            return 75 + _rng.NextInt(425); // outsized trade
        }

        return 1 + _rng.NextInt(15);
    }
}
