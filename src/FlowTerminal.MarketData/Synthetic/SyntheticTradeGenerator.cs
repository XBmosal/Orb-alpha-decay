using FlowTerminal.Domain.Events;

namespace FlowTerminal.MarketData.Synthetic;

/// <summary>Receives the book mutations and executions the generators produce.</summary>
internal interface IBookEventSink
{
    /// <summary>A resting level's displayed size changed (0 ⇒ the level was removed).</summary>
    void OnDepthChanged(Side side, long price, long displayed);

    /// <summary>An aggressive order executed <paramref name="qty"/> at <paramref name="price"/>.</summary>
    void OnExecution(AggressorSide aggressor, long price, long qty);
}

/// <summary>
/// Generates aggressive trades <em>from the book</em>. Every clip is matched against
/// real resting liquidity: it lifts asks (buy) or hits bids (sell) from the touch
/// outward, consuming levels strictly in price order (no skipping), printing one
/// execution per price it trades through, and reducing/removing the levels it eats.
/// When a touch level is fully consumed the best price moves — that is the only way
/// (together with re-quoting) the synthetic price changes, so price is emergent, not
/// an external walk. Clip sizes are heavy-tailed; rare outsized clips sweep.
/// </summary>
public sealed class SyntheticTradeGenerator
{
    private readonly SyntheticMarketConfiguration _cfg;

    public SyntheticTradeGenerator(SyntheticMarketConfiguration cfg) => _cfg = cfg;

    /// <summary>Net executed quantity (buy positive) produced on the most recent step.</summary>
    public long LastNetFlow { get; private set; }

    internal void Generate(SyntheticOrderBook book, in RegimeProfile p, double buyBias,
        ref DeterministicRng rng, IBookEventSink sink)
    {
        LastNetFlow = 0;
        double lambda = _cfg.TradeRate * p.TradeMult;
        int clips = SampleClipCount(lambda, ref rng);
        if (clips == 0)
        {
            return;
        }

        double pBuy = Math.Clamp(0.5 + 0.42 * buyBias + 0.06 * SynthDistributions.NextGaussian(ref rng), 0.04, 0.96);

        for (int c = 0; c < clips; c++)
        {
            bool buy = rng.NextDouble() < pBuy;
            long size = ClipSize(in p, ref rng);
            if (size <= 0)
            {
                continue;
            }

            if (buy)
            {
                Sweep(book, Side.Ask, AggressorSide.Buy, size, ref rng, sink, ascending: true);
            }
            else
            {
                Sweep(book, Side.Bid, AggressorSide.Sell, size, ref rng, sink, ascending: false);
            }
        }
    }

    /// <summary>
    /// Executes a single aggressive order of the given size against the book, used by
    /// the orchestrator and exercised directly in tests. A buy lifts asks, a sell hits
    /// bids, always from the touch outward in strict price order.
    /// </summary>
    internal void ExecuteOrder(SyntheticOrderBook book, AggressorSide aggressor, long size,
        ref DeterministicRng rng, IBookEventSink sink)
    {
        LastNetFlow = 0;
        if (aggressor == AggressorSide.Buy)
        {
            Sweep(book, Side.Ask, AggressorSide.Buy, size, ref rng, sink, ascending: true);
        }
        else
        {
            Sweep(book, Side.Bid, AggressorSide.Sell, size, ref rng, sink, ascending: false);
        }
    }

    private void Sweep(SyntheticOrderBook book, Side restingSide, AggressorSide aggressor,
        long size, ref DeterministicRng rng, IBookEventSink sink, bool ascending)
    {
        long remaining = size;
        int levels = 0;

        while (remaining > 0 && levels < _cfg.MaxSweepLevels)
        {
            long price = restingSide == Side.Ask ? book.BestAskTicks : book.BestBidTicks;
            if (price == SyntheticOrderBook.NoPrice)
            {
                break; // nothing left to trade against this step
            }

            var lvl = book.Find(restingSide, price);
            if (lvl is null)
            {
                break;
            }

            long fill = Math.Min(remaining, lvl.Displayed);
            if (fill <= 0)
            {
                break;
            }

            lvl.Displayed -= fill;
            lvl.ExecutedTotal += fill;
            remaining -= fill;
            levels++;
            LastNetFlow += aggressor == AggressorSide.Buy ? fill : -fill;

            sink.OnExecution(aggressor, price, fill);

            if (lvl.Displayed <= 0)
            {
                book.Remove(restingSide, price);
                sink.OnDepthChanged(restingSide, price, 0);
            }
            else
            {
                lvl.LastChangeUtc = lvl.CreatedUtc; // touched this step
                sink.OnDepthChanged(restingSide, price, lvl.Displayed);
                break; // partial fill of the touch — order is done, no need to walk further
            }

            // Continue only if the order is outsized enough to keep eating (sweep).
            if (remaining > 0 && !SynthDistributions.Chance(ref rng, 0.85))
            {
                break;
            }

            _ = ascending; // direction is encoded by best-price recompute in the book
        }
    }

    private int SampleClipCount(double lambda, ref DeterministicRng rng)
    {
        // Cheap, bounded approximation: integer part fires for sure, fractional part
        // is a coin flip, with a small chance of a burst in busy regimes.
        int n = (int)lambda;
        if (rng.NextDouble() < lambda - n) n++;
        if (lambda > 1.2 && rng.NextDouble() < 0.15) n++;
        return Math.Min(n, 5);
    }

    private long ClipSize(in RegimeProfile p, ref DeterministicRng rng)
    {
        if (SynthDistributions.Chance(ref rng, _cfg.LargeTradeProbability * p.SweepMult))
        {
            double mult = SynthDistributions.Lerp(ref rng, _cfg.LargeTradeMultMin, _cfg.LargeTradeMultMax);
            return Math.Max(1, (long)Math.Round(_cfg.TradeSizeMedian * mult));
        }

        return SynthDistributions.Size(ref rng, _cfg.TradeSizeMedian, _cfg.TradeSizeSigma, 1.0, 1);
    }
}
