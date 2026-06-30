using FlowTerminal.Domain.Events;
using FlowTerminal.Domain.Instruments;

namespace FlowTerminal.Analytics.BigTrades;

/// <summary>Lightweight counters describing what the detector has processed (for diagnostics).</summary>
public readonly record struct BigTradeDiagnostics(
    long TradesProcessed, long BuyTrades, long SellTrades, long UnknownTrades,
    long NativeClassifications, long EstimatedClassifications, long InvalidBookClassifications,
    long GroupsFinalized, long Sweeps, long CurrentThreshold, bool WarmedUp);

/// <summary>
/// The shared Big Trades engine: classifies each trade's aggressor side, maintains the
/// size distribution, aggregates related aggressive prints into groups, decides which
/// groups are "big" under the active threshold, and flags multi-level sweeps. One
/// instance is the single source of truth that the chart bubbles, heatmap, footprint,
/// time-and-sales, and DOM highlighting all reconcile against. Deterministic and
/// single-writer, so replay reproduces identical groups and IDs. Strictly observational.
/// </summary>
public sealed class BigTradeDetector
{
    private readonly BigTradeSettings _settings;
    private readonly AggressorClassifier _classifier = new();
    private readonly TradeThresholdEngine _threshold;
    private readonly List<BigTradeGroup> _finalized = new();
    private readonly int _ringCap;

    private Open? _open;
    private long _fallbackSeq;

    // Diagnostics counters.
    private long _trades, _buys, _sells, _unknown, _native, _estimated, _invalidBook, _groups, _sweeps;
    private long _lastThreshold;
    private bool _warmedUp;

    public BigTradeDetector(BigTradeSettings? settings = null, int ringCapacity = 4000)
    {
        _settings = (settings ?? BigTradePresetRegistry.Default.Settings).Validate();
        _threshold = new TradeThresholdEngine(_settings);
        _ringCap = Math.Max(16, ringCapacity);
    }

    public BigTradeSettings Settings => _settings;

    public static BigTradeDetector For(RootSymbol root) =>
        new(BigTradePresetRegistry.ForInstrument(root).Settings);

    /// <summary>
    /// Processes one canonical event. Non-trade events update the classifier's view only.
    /// Returns the per-trade classification (so callers can build correctly-coloured
    /// per-trade markers); aggregated groups are read via <see cref="Snapshot"/>.
    /// </summary>
    public ClassifiedSide OnTrade(in MarketEvent e, long bestBidTicks, long bestAskTicks, bool bookValid)
    {
        if (e.Type != MarketEventType.Trade || e.Quantity <= 0)
            return new ClassifiedSide(AggressorSide.Unknown, ClassificationQuality.Unknown);

        var classified = _classifier.Classify(e, bestBidTicks, bestAskTicks, bookValid);

        _trades++;
        switch (classified.Side)
        {
            case AggressorSide.Buy: _buys++; break;
            case AggressorSide.Sell: _sells++; break;
            default: _unknown++; break;
        }
        if (classified.Quality == ClassificationQuality.Native) _native++;
        else if (classified.Quality.IsEstimated()) _estimated++;
        else if (classified.Quality == ClassificationQuality.InvalidBook) _invalidBook++;

        if (e.Quantity < _settings.MinTradeQuantity)
            return classified;

        // Feed the size distribution with every qualifying trade (not only big ones).
        _threshold.Observe(e.Quantity);

        long seq = e.HasFlag(MarketEventFlags.HasExchangeSequence) && e.ExchangeSequence != 0
            ? e.ExchangeSequence
            : ++_fallbackSeq;

        if (_settings.Aggregation == AggregationMode.None)
        {
            FinalizeAndStore(NewOpen(e, classified, seq));
            return classified;
        }

        if (_open is { } open && Continues(open, classified.Side, e.PriceTicks, e.ExchangeTimestampUtc))
        {
            Extend(open, e, classified, seq);
        }
        else
        {
            if (_open is { } prev) FinalizeAndStore(prev);
            _open = NewOpen(e, classified, seq);
        }

        return classified;
    }

    /// <summary>Finalizes any open group (call at end of stream / before a determinism hash).</summary>
    public void Flush()
    {
        if (_open is { } open)
        {
            FinalizeAndStore(open);
            _open = null;
        }
    }

    public void ResetSession()
    {
        Flush();
        _threshold.ResetSession();
    }

    /// <summary>Full reset for a contract/timeframe change or replay seek.</summary>
    public void Reset()
    {
        _open = null;
        _finalized.Clear();
        _classifier.Reset();
        _threshold.Reset();
        _fallbackSeq = 0;
        _trades = _buys = _sells = _unknown = _native = _estimated = _invalidBook = _groups = _sweeps = 0;
        _lastThreshold = 0;
        _warmedUp = false;
    }

    /// <summary>
    /// The Big Trade groups to display: finalized groups within <paramref name="maxAgeMs"/>
    /// of <paramref name="nowUtc"/> plus the currently-forming group if it already
    /// qualifies, honouring the show-buys/sells/unknown filters, newest last.
    /// </summary>
    public IReadOnlyList<BigTradeGroup> Snapshot(DateTime nowUtc, int maxAgeMs = int.MaxValue, int maxCount = 4000)
    {
        var result = new List<BigTradeGroup>(Math.Min(maxCount, _finalized.Count + 1));
        DateTime cutoff = maxAgeMs == int.MaxValue ? DateTime.MinValue : nowUtc.AddMilliseconds(-maxAgeMs);

        for (int i = _finalized.Count - 1; i >= 0 && result.Count < maxCount; i--)
        {
            var g = _finalized[i];
            if (g.EndUtc < cutoff) break;            // older entries are even older
            if (Allowed(g.Side)) result.Add(g);
        }
        result.Reverse();

        // Include the forming group as a live preview if it already qualifies.
        if (_open is { } open && result.Count < maxCount)
        {
            var preview = TryFinalize(open, store: false);
            if (preview is { } g && Allowed(g.Side) && g.EndUtc >= cutoff) result.Add(g);
        }

        return result;
    }

    public BigTradeDiagnostics DiagnosticsSnapshot() => new(
        _trades, _buys, _sells, _unknown, _native, _estimated, _invalidBook, _groups, _sweeps, _lastThreshold, _warmedUp);

    // ── Aggregation internals ────────────────────────────────────────────

    private sealed class Open
    {
        public AggressorSide Side;
        public ClassificationQuality WorstQuality;
        public bool AnyEstimated;
        public DateTime Start, Last;
        public long FirstPrice, LastPrice, MinPrice, MaxPrice;
        public long Total, Largest;
        public int Count, DistinctLevels;
        public double WeightedNum;        // Σ price·qty
        public long SeqStart, SeqEnd;
        public bool Monotonic = true;
        public long PrevDistinctPrice;
        public bool HasPrevDistinct;
    }

    private Open NewOpen(in MarketEvent e, ClassifiedSide c, long seq)
    {
        var o = new Open
        {
            Side = c.Side,
            WorstQuality = c.Quality,
            AnyEstimated = c.IsEstimated,
            Start = e.ExchangeTimestampUtc,
            Last = e.ExchangeTimestampUtc,
            FirstPrice = e.PriceTicks,
            LastPrice = e.PriceTicks,
            MinPrice = e.PriceTicks,
            MaxPrice = e.PriceTicks,
            Total = e.Quantity,
            Largest = e.Quantity,
            Count = 1,
            DistinctLevels = 1,
            WeightedNum = (double)e.PriceTicks * e.Quantity,
            SeqStart = seq,
            SeqEnd = seq,
            PrevDistinctPrice = e.PriceTicks,
            HasPrevDistinct = true,
        };
        return o;
    }

    private void Extend(Open o, in MarketEvent e, ClassifiedSide c, long seq)
    {
        if (e.PriceTicks != o.LastPrice)
        {
            // Track monotonic progression in the aggressive direction for honest sweeps.
            bool inDirection = o.Side == AggressorSide.Buy ? e.PriceTicks >= o.PrevDistinctPrice
                : o.Side == AggressorSide.Sell ? e.PriceTicks <= o.PrevDistinctPrice
                : false;
            if (!inDirection) o.Monotonic = false;
            o.PrevDistinctPrice = e.PriceTicks;
            o.DistinctLevels++;
        }

        o.Last = e.ExchangeTimestampUtc;
        o.LastPrice = e.PriceTicks;
        o.MinPrice = Math.Min(o.MinPrice, e.PriceTicks);
        o.MaxPrice = Math.Max(o.MaxPrice, e.PriceTicks);
        o.Total += e.Quantity;
        o.Largest = Math.Max(o.Largest, e.Quantity);
        o.Count++;
        o.WeightedNum += (double)e.PriceTicks * e.Quantity;
        o.SeqEnd = seq;
        if (c.IsEstimated) o.AnyEstimated = true;
        // Representative quality is the "worst" (highest enum value among estimated states).
        if (c.Quality.IsEstimated() && !o.WorstQuality.IsEstimated()) o.WorstQuality = c.Quality;
    }

    private bool Continues(Open o, AggressorSide side, long price, DateTime ts)
    {
        if (_settings.RequireSameSide && side != o.Side) return false;
        if ((ts - o.Last).TotalMilliseconds > _settings.TimeWindowMs) return false;
        if ((ts - o.Start).TotalMilliseconds > _settings.MaxGroupDurationMs) return false;

        return _settings.Aggregation switch
        {
            AggregationMode.SamePrice => price == o.FirstPrice,
            AggregationMode.AdjacentPrice => Math.Abs(price - o.LastPrice) <= _settings.MaxTickDistance,
            AggregationMode.Sweep => Progresses(o.Side, price, o.LastPrice)
                                     && Math.Abs(price - o.LastPrice) <= Math.Max(_settings.MaxTickDistance, 1),
            _ => false,
        };
    }

    private static bool Progresses(AggressorSide side, long price, long lastPrice) => side switch
    {
        AggressorSide.Buy => price >= lastPrice,
        AggressorSide.Sell => price <= lastPrice,
        _ => price == lastPrice,
    };

    private void FinalizeAndStore(Open o)
    {
        var g = TryFinalize(o, store: true);
        if (g is null) return;
        _finalized.Add(g);
        if (_finalized.Count > _ringCap) _finalized.RemoveRange(0, _finalized.Count - _ringCap);
    }

    private BigTradeGroup? TryFinalize(Open o, bool store)
    {
        var result = _threshold.Evaluate(o.Total, o.Side);
        if (store)
        {
            _lastThreshold = result.ThresholdUsed;
            _warmedUp = result.WarmedUp;
        }
        if (!result.IsLarge) return null;

        long weighted = o.Total > 0 ? (long)Math.Round(o.WeightedNum / o.Total) : o.FirstPrice;
        bool isSweep = o.Monotonic && o.DistinctLevels >= _settings.SweepMinLevels && o.Total >= _settings.SweepMinVolume
                       && o.Side != AggressorSide.Unknown;

        if (store)
        {
            _groups++;
            if (isSweep) _sweeps++;
        }

        var quality = o.AnyEstimated ? (o.WorstQuality.IsEstimated() ? o.WorstQuality : ClassificationQuality.InferredBidAsk)
            : (o.Side == AggressorSide.Unknown ? ClassificationQuality.Unknown : ClassificationQuality.Native);

        ulong id = GroupId(o.SeqStart, o.Start, o.FirstPrice, o.Side);

        return new BigTradeGroup(
            id, o.Side, quality, o.Start, o.Last,
            PriceTicks: weighted, MinPriceTicks: o.MinPrice, MaxPriceTicks: o.MaxPrice,
            TotalQuantity: o.Total, TradeCount: o.Count, LargestTrade: o.Largest,
            ThresholdUsed: result.ThresholdUsed, ThresholdMode: _settings.Mode,
            PercentileRank: result.PercentileRank, ZScore: result.ZScore,
            IsSweep: isSweep, SweepLevels: isSweep ? o.DistinctLevels : 0,
            IsAggregated: o.Count > 1, SequenceStart: o.SeqStart, SequenceEnd: o.SeqEnd);
    }

    private bool Allowed(AggressorSide side) => side switch
    {
        AggressorSide.Buy => _settings.ShowBuys,
        AggressorSide.Sell => _settings.ShowSells,
        _ => _settings.ShowUnknown,
    };

    /// <summary>Deterministic 64-bit group id (FNV-1a over the defining fields).</summary>
    private static ulong GroupId(long seqStart, DateTime startUtc, long firstPrice, AggressorSide side)
    {
        unchecked
        {
            ulong h = 14695981039346656037UL;
            void Mix(long v) { for (int b = 0; b < 8; b++) { h ^= (byte)(v >> (b * 8)); h *= 1099511628211UL; } }
            Mix(seqStart);
            Mix(startUtc.Ticks);
            Mix(firstPrice);
            h ^= (byte)side; h *= 1099511628211UL;
            return h;
        }
    }

    /// <summary>
    /// Deterministic hash of a sequence of finalized groups' observable fields, for replay
    /// verification: the same recorded trades + settings reproduce the same value.
    /// </summary>
    public static ulong Hash(IReadOnlyList<BigTradeGroup> groups)
    {
        unchecked
        {
            ulong h = 14695981039346656037UL;
            void Mix(long v) { for (int b = 0; b < 8; b++) { h ^= (byte)(v >> (b * 8)); h *= 1099511628211UL; } }
            foreach (var g in groups)
            {
                Mix((long)g.Id);
                Mix((byte)g.Side);
                Mix((byte)g.Quality);
                Mix(g.PriceTicks);
                Mix(g.TotalQuantity);
                Mix(g.TradeCount);
                Mix(g.LargestTrade);
                Mix(g.StartUtc.Ticks);
                Mix(g.EndUtc.Ticks);
                Mix(g.IsSweep ? 1 : 0);
                Mix(g.SweepLevels);
                Mix((byte)g.ThresholdMode);
            }
            return h;
        }
    }
}
