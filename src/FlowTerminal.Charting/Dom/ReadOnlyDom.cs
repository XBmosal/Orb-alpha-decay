using FlowTerminal.Analytics.Profiles;
using FlowTerminal.Domain.Events;
using FlowTerminal.OrderBook;

namespace FlowTerminal.Charting.Dom;

/// <summary>
/// One row of the read-only depth-of-market ladder. This is observational data
/// only — there are no order-entry, quantity, working-order, position, or P&amp;L
/// fields, by design. Prices are integer ticks.
/// </summary>
public readonly record struct DomRow(
    long PriceTicks,
    long BidSize,
    long AskSize,
    long CumulativeBid,
    long CumulativeAsk,
    long TradedAtBid,
    long TradedAtAsk,
    long TotalTraded,
    long Delta,
    bool IsPoc,
    bool IsValueAreaHigh,
    bool IsValueAreaLow,
    bool IsBestBid = false,
    bool IsBestAsk = false,
    long DistanceTicks = 0,
    long BidPulled = 0,
    long BidStacked = 0,
    long AskPulled = 0,
    long AskStacked = 0,
    int BidReplenish = 0,
    int AskReplenish = 0,
    bool IsBidWall = false,
    bool IsAskWall = false);

/// <summary>
/// Builds a read-only DOM ladder from the canonical market-by-price book plus a
/// session volume profile (the same book/trades the heatmap and footprint consume).
/// The ladder is a snapshot — it never mutates the book and exposes no way to act on
/// it. Strictly observational.
///
/// Conventions (documented and tested):
///   - Cumulative depth accumulates <b>from the touch outward</b>: cumulative ask at
///     price P (P ≥ best ask) is the sum of ask sizes from the best ask up to P;
///     cumulative bid at price P (P ≤ best bid) is the sum from the best bid down to P.
///   - Executed volume comes from the trade-driven profile (buys = traded at ask,
///     sells = traded at bid) — never inferred from depth changes.
///   - Pulling/stacking/replenishment (optional) come from <see cref="PullStackTracker"/>.
///   - A level is a "wall" when its displayed size is unusually large relative to the
///     visible side (≥ 3× the visible median) and clears an absolute floor.
/// </summary>
public static class ReadOnlyDom
{
    public static IReadOnlyList<DomRow> Build(IOrderBook book, VolumeProfile profile, int levels) =>
        Build(book, profile, levels, tracker: null, wallFloor: long.MaxValue);

    /// <summary>
    /// Builds the ladder for <paramref name="levels"/> price steps either side of the
    /// mid, top (highest price) first. Returns an empty list when the book has no
    /// established best bid/ask. When <paramref name="tracker"/> is supplied the
    /// pulling/stacking/replenishment fields are populated; <paramref name="wallFloor"/>
    /// is the absolute minimum size a level must reach to be flagged a wall.
    /// </summary>
    public static IReadOnlyList<DomRow> Build(
        IOrderBook book, VolumeProfile profile, int levels, PullStackTracker? tracker, long wallFloor)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(profile);
        if (levels < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(levels));
        }

        long bestBid = book.BestBidTicks;
        long bestAsk = book.BestAskTicks;
        if (bestBid == BookSide.NoPrice || bestAsk == BookSide.NoPrice)
        {
            return Array.Empty<DomRow>();
        }

        long mid = (bestBid + bestAsk) / 2;
        long top = mid + levels;
        long bottom = mid - levels;

        long poc = profile.PocTicks();
        var va = profile.ComputeValueArea();

        // Cumulative depth, touch-outward. Ask cum accumulates upward from best ask;
        // bid cum accumulates downward from best bid.
        var cumAskMap = new Dictionary<long, long>();
        long run = 0;
        for (long price = bestAsk; price <= top; price++)
        {
            run += book.SizeAt(Side.Ask, price);
            cumAskMap[price] = run;
        }

        var cumBidMap = new Dictionary<long, long>();
        run = 0;
        for (long price = bestBid; price >= bottom; price--)
        {
            run += book.SizeAt(Side.Bid, price);
            cumBidMap[price] = run;
        }

        // Wall thresholds: 3× the visible non-zero median per side, clearing the floor.
        long bidWall = WallThreshold(book, Side.Bid, bestBid, bottom, wallFloor);
        long askWall = WallThreshold(book, Side.Ask, bestAsk, top, wallFloor);

        var rows = new List<DomRow>((int)(top - bottom + 1));
        for (long price = top; price >= bottom; price--)
        {
            long bid = book.SizeAt(Side.Bid, price);
            long ask = book.SizeAt(Side.Ask, price);
            long tradedAtBid = profile.SellVolumeAt(price);
            long tradedAtAsk = profile.BuyVolumeAt(price);

            long distance = price >= bestAsk ? price - bestAsk
                : price <= bestBid ? bestBid - price : 0;

            rows.Add(new DomRow(
                price, bid, ask,
                CumulativeBid: cumBidMap.GetValueOrDefault(price),
                CumulativeAsk: cumAskMap.GetValueOrDefault(price),
                TradedAtBid: tradedAtBid,
                TradedAtAsk: tradedAtAsk,
                TotalTraded: tradedAtBid + tradedAtAsk,
                Delta: tradedAtAsk - tradedAtBid,
                IsPoc: price == poc,
                IsValueAreaHigh: price == va.VahTicks,
                IsValueAreaLow: price == va.ValTicks,
                IsBestBid: price == bestBid,
                IsBestAsk: price == bestAsk,
                DistanceTicks: distance,
                BidPulled: tracker?.PulledAt(Side.Bid, price) ?? 0,
                BidStacked: tracker?.StackedAt(Side.Bid, price) ?? 0,
                AskPulled: tracker?.PulledAt(Side.Ask, price) ?? 0,
                AskStacked: tracker?.StackedAt(Side.Ask, price) ?? 0,
                BidReplenish: tracker?.ReplenishmentsAt(Side.Bid, price) ?? 0,
                AskReplenish: tracker?.ReplenishmentsAt(Side.Ask, price) ?? 0,
                IsBidWall: price <= bestBid && bid >= bidWall,
                IsAskWall: price >= bestAsk && ask >= askWall));
        }

        return rows;
    }

    /// <summary>
    /// A deterministic 64-bit hash (FNV-1a) of a DOM snapshot's observable fields, in row
    /// order. Two runs over the same recorded data produce the same value, so replays can
    /// assert the DOM reconstructed identically. Order-sensitive by construction.
    /// </summary>
    public static ulong Hash(IReadOnlyList<DomRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        ulong h = offset;

        static ulong Mix(ulong h, long v)
        {
            unchecked
            {
                for (int b = 0; b < 8; b++)
                {
                    h ^= (byte)(v >> (b * 8));
                    h *= 1099511628211UL;
                }
            }
            return h;
        }

        unchecked
        {
            foreach (var r in rows)
            {
                h = Mix(h, r.PriceTicks);
                h = Mix(h, r.BidSize);
                h = Mix(h, r.AskSize);
                h = Mix(h, r.CumulativeBid);
                h = Mix(h, r.CumulativeAsk);
                h = Mix(h, r.TradedAtBid);
                h = Mix(h, r.TradedAtAsk);
                h = Mix(h, r.Delta);
                h = Mix(h, r.BidPulled);
                h = Mix(h, r.BidStacked);
                h = Mix(h, r.AskPulled);
                h = Mix(h, r.AskStacked);
                h = Mix(h, r.BidReplenish);
                h = Mix(h, r.AskReplenish);
                // Pack the boolean flags into one byte so flag changes alter the hash.
                long flags =
                    (r.IsPoc ? 1 : 0) | (r.IsValueAreaHigh ? 2 : 0) | (r.IsValueAreaLow ? 4 : 0) |
                    (r.IsBestBid ? 8 : 0) | (r.IsBestAsk ? 16 : 0) | (r.IsBidWall ? 32 : 0) | (r.IsAskWall ? 64 : 0);
                h ^= (byte)flags;
                h *= prime;
            }
        }
        return h;
    }

    private static long WallThreshold(IOrderBook book, Side side, long touch, long edge, long floor)
    {
        var sizes = new List<long>();
        if (side == Side.Bid)
            for (long p = touch; p >= edge; p--) { long s = book.SizeAt(Side.Bid, p); if (s > 0) sizes.Add(s); }
        else
            for (long p = touch; p <= edge; p++) { long s = book.SizeAt(Side.Ask, p); if (s > 0) sizes.Add(s); }

        if (sizes.Count == 0) return long.MaxValue;
        sizes.Sort();
        long median = sizes[sizes.Count / 2];
        long relative = median * 3;
        return Math.Max(relative, floor == long.MaxValue ? relative : floor);
    }
}
