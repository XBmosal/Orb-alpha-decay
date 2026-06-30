using FlowTerminal.Domain.Events;

namespace FlowTerminal.MarketData.Synthetic;

/// <summary>
/// A single resting price level in the synthetic book, carrying the full lifecycle
/// state a realistic level needs: displayed size, cumulative add/cancel/execute,
/// age, persistence (stickiness), replenishment count, an outsized-wall flag, a
/// persistent per-level size identity and the size it tends to return to.
/// </summary>
public sealed class SyntheticLevel
{
    public long PriceTicks;
    public long Displayed;
    public long AddedTotal;
    public long CanceledTotal;
    public long ExecutedTotal;
    public long TargetSize;
    public int ReplenishCount;
    public int AgeSteps;
    public bool IsWall;
    public double Persistence;
    public double Noise;
    public DateTime CreatedUtc;
    public DateTime LastChangeUtc;
}

/// <summary>
/// The stateful market-by-price book the synthetic engine mutates incrementally.
/// It is the single source of truth for liquidity; the canonical event stream is
/// derived from its mutations, and the heatmap therefore renders genuine book
/// history. The book enforces its core integrity invariant itself:
/// <c>BestBidTicks &lt; BestAskTicks</c> is never violated.
/// </summary>
public sealed class SyntheticOrderBook
{
    public const long NoPrice = long.MinValue;

    private readonly Dictionary<long, SyntheticLevel> _bids = new();
    private readonly Dictionary<long, SyntheticLevel> _asks = new();

    public long BestBidTicks { get; private set; } = NoPrice;
    public long BestAskTicks { get; private set; } = NoPrice;

    public int BidLevelCount => _bids.Count;
    public int AskLevelCount => _asks.Count;

    public IReadOnlyDictionary<long, SyntheticLevel> Bids => _bids;
    public IReadOnlyDictionary<long, SyntheticLevel> Asks => _asks;

    public bool IsCrossed =>
        BestBidTicks != NoPrice && BestAskTicks != NoPrice && BestBidTicks >= BestAskTicks;

    private Dictionary<long, SyntheticLevel> Map(Side side) => side == Side.Bid ? _bids : _asks;

    public SyntheticLevel? Find(Side side, long price) =>
        Map(side).TryGetValue(price, out var lvl) ? lvl : null;

    public long DisplayedAt(Side side, long price) =>
        Map(side).TryGetValue(price, out var lvl) ? lvl.Displayed : 0;

    /// <summary>
    /// Inserts or replaces a level. A bid that would be ≥ best ask (or an ask ≤ best
    /// bid) is rejected to preserve the non-crossed invariant; callers treat a false
    /// return as a no-op (the proposed event is dropped, not applied).
    /// </summary>
    public bool Put(Side side, SyntheticLevel level)
    {
        if (level.Displayed <= 0)
        {
            return Remove(side, level.PriceTicks);
        }

        if (side == Side.Bid && BestAskTicks != NoPrice && level.PriceTicks >= BestAskTicks)
        {
            return false;
        }

        if (side == Side.Ask && BestBidTicks != NoPrice && level.PriceTicks <= BestBidTicks)
        {
            return false;
        }

        Map(side)[level.PriceTicks] = level;
        if (side == Side.Bid)
        {
            if (BestBidTicks == NoPrice || level.PriceTicks > BestBidTicks) BestBidTicks = level.PriceTicks;
        }
        else
        {
            if (BestAskTicks == NoPrice || level.PriceTicks < BestAskTicks) BestAskTicks = level.PriceTicks;
        }

        return true;
    }

    public bool Remove(Side side, long price)
    {
        var map = Map(side);
        if (!map.Remove(price))
        {
            return false;
        }

        if (side == Side.Bid && price == BestBidTicks) BestBidTicks = MaxKey(_bids);
        else if (side == Side.Ask && price == BestAskTicks) BestAskTicks = MinKey(_asks);
        return true;
    }

    private static long MaxKey(Dictionary<long, SyntheticLevel> m)
    {
        long best = NoPrice;
        foreach (var k in m.Keys) if (best == NoPrice || k > best) best = k;
        return best;
    }

    private static long MinKey(Dictionary<long, SyntheticLevel> m)
    {
        long best = NoPrice;
        foreach (var k in m.Keys) if (best == NoPrice || k < best) best = k;
        return best;
    }
}
