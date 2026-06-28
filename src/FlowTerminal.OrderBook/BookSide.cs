using System.Collections;
using FlowTerminal.Domain.Events;

namespace FlowTerminal.OrderBook;

/// <summary>
/// One aggregated side of a market-by-price book: price (in ticks) → resting size.
/// Bids are ordered best-first (highest price first); asks best-first (lowest
/// price first). The best price is cached and only recomputed when the current
/// best level is removed, so the hot path stays allocation-free and free of LINQ.
/// </summary>
public sealed class BookSide
{
    public const long NoPrice = long.MinValue;

    private readonly SortedDictionary<long, long> _levels;
    private readonly bool _bestIsHighest;
    private long _bestPrice = NoPrice;

    public BookSide(Side side)
    {
        Side = side;
        _bestIsHighest = side == Side.Bid;
        // Bids sort descending so the first key is the best (highest) bid.
        _levels = _bestIsHighest
            ? new SortedDictionary<long, long>(DescendingComparer.Instance)
            : new SortedDictionary<long, long>();
    }

    public Side Side { get; }

    public int LevelCount => _levels.Count;

    /// <summary>Best price on this side, or <see cref="NoPrice"/> when empty.</summary>
    public long BestPrice => _bestPrice;

    public long SizeAt(long priceTicks) => _levels.TryGetValue(priceTicks, out var size) ? size : 0;

    /// <summary>
    /// Sets the aggregated size at a price level. Size 0 removes the level. Negative
    /// sizes are rejected (returns false) so the book can never hold negative depth.
    /// </summary>
    public bool SetLevel(long priceTicks, long size)
    {
        if (size < 0)
        {
            return false;
        }

        if (size == 0)
        {
            if (_levels.Remove(priceTicks) && priceTicks == _bestPrice)
            {
                RecomputeBest();
            }

            return true;
        }

        _levels[priceTicks] = size;
        if (_bestPrice == NoPrice || IsBetter(priceTicks, _bestPrice))
        {
            _bestPrice = priceTicks;
        }

        return true;
    }

    public void Clear()
    {
        _levels.Clear();
        _bestPrice = NoPrice;
    }

    /// <summary>Cumulative size from the best price through (and including) the given depth count.</summary>
    public long CumulativeSize(int levels)
    {
        long total = 0;
        int i = 0;
        foreach (var kv in _levels)
        {
            if (i++ >= levels)
            {
                break;
            }

            total += kv.Value;
        }

        return total;
    }

    /// <summary>Enumerates levels best-first as (priceTicks, size).</summary>
    public IEnumerable<KeyValuePair<long, long>> Levels() => _levels;

    private bool IsBetter(long candidate, long current) =>
        _bestIsHighest ? candidate > current : candidate < current;

    private void RecomputeBest()
    {
        foreach (var key in _levels.Keys)
        {
            _bestPrice = key; // first key is the best due to the comparer
            return;
        }

        _bestPrice = NoPrice;
    }

    private sealed class DescendingComparer : IComparer<long>
    {
        public static readonly DescendingComparer Instance = new();

        public int Compare(long x, long y) => y.CompareTo(x);
    }
}
