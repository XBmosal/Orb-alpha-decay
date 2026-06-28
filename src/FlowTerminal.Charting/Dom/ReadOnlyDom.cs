using FlowTerminal.Analytics.Profiles;
using FlowTerminal.Domain.Events;
using FlowTerminal.OrderBook;

namespace FlowTerminal.Charting.Dom;

/// <summary>
/// One row of the read-only depth-of-market ladder. This is observational data
/// only — there are no order-entry, quantity, working-order, position, or P&amp;L
/// fields, by design.
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
    bool IsValueAreaLow);

/// <summary>
/// Builds a read-only DOM ladder from a market-by-price book plus a session volume
/// profile. The ladder is a snapshot — it never mutates the book and exposes no way
/// to act on it. Strictly observational.
/// </summary>
public static class ReadOnlyDom
{
    /// <summary>
    /// Builds the ladder for <paramref name="levels"/> price steps either side of the
    /// mid, top (highest price) first. Returns an empty list when the book has no
    /// established best bid/ask.
    /// </summary>
    public static IReadOnlyList<DomRow> Build(IOrderBook book, VolumeProfile profile, int levels)
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

        long cumBid = 0;
        long cumAsk = 0;
        // Cumulative depth accumulates from the inside (best) outward; precompute by walking.
        var rows = new List<DomRow>((int)(top - bottom + 1));
        for (long price = top; price >= bottom; price--)
        {
            long bid = book.SizeAt(Side.Bid, price);
            long ask = book.SizeAt(Side.Ask, price);
            cumAsk += ask; // asks accumulate downward toward the inside
            long tradedAtBid = profile.SellVolumeAt(price);
            long tradedAtAsk = profile.BuyVolumeAt(price);

            rows.Add(new DomRow(
                price, bid, ask,
                CumulativeBid: 0, // filled in the second pass
                CumulativeAsk: cumAsk,
                TradedAtBid: tradedAtBid,
                TradedAtAsk: tradedAtAsk,
                TotalTraded: tradedAtBid + tradedAtAsk,
                Delta: tradedAtAsk - tradedAtBid,
                IsPoc: price == poc,
                IsValueAreaHigh: price == va.VahTicks,
                IsValueAreaLow: price == va.ValTicks));
        }

        // Second pass bottom-up for cumulative bid depth (accumulates upward toward inside).
        for (int i = rows.Count - 1; i >= 0; i--)
        {
            cumBid += rows[i].BidSize;
            rows[i] = rows[i] with { CumulativeBid = cumBid };
        }

        return rows;
    }
}
