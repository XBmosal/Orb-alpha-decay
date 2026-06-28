using FlowTerminal.Domain.Events;

namespace FlowTerminal.OrderBook;

/// <summary>
/// A read-only, reconstructed limit order book. It is observational only — it has
/// no concept of the user's own orders, queue position, or execution, because the
/// application never submits orders.
///
/// The concrete market-by-price and market-by-order engines are implemented in
/// Phase 2. This interface fixes the contract the analytics and DOM layers build on.
/// </summary>
public interface IOrderBook
{
    /// <summary>True once a valid snapshot has been applied and no unresolved gap exists.</summary>
    bool IsValid { get; }

    long BestBidTicks { get; }

    long BestAskTicks { get; }

    /// <summary>Aggregated resting size at a price level for the given side (0 if none).</summary>
    long SizeAt(Side side, long priceTicks);

    /// <summary>Applies one canonical event, mutating book state. Returns false if it invalidated the book.</summary>
    bool Apply(in MarketEvent marketEvent);

    /// <summary>Clears all state (book clear, reconnect, contract change).</summary>
    void Clear();
}
