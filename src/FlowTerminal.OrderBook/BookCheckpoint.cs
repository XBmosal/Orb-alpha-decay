using FlowTerminal.Domain.Events;

namespace FlowTerminal.OrderBook;

/// <summary>
/// An immutable snapshot of a market-by-price book at a point in time. Checkpoints
/// let replay seek efficiently: rather than replaying from session start, replay
/// restores the nearest checkpoint and applies only the events after it.
/// </summary>
public sealed class BookCheckpoint
{
    public BookCheckpoint(
        long exchangeSequence,
        DateTime asOfUtc,
        IReadOnlyList<PriceLevel> bids,
        IReadOnlyList<PriceLevel> asks,
        bool isValid)
    {
        ExchangeSequence = exchangeSequence;
        AsOfUtc = asOfUtc;
        Bids = bids;
        Asks = asks;
        IsValid = isValid;
    }

    public long ExchangeSequence { get; }

    public DateTime AsOfUtc { get; }

    /// <summary>Bid levels, best-first.</summary>
    public IReadOnlyList<PriceLevel> Bids { get; }

    /// <summary>Ask levels, best-first.</summary>
    public IReadOnlyList<PriceLevel> Asks { get; }

    public bool IsValid { get; }
}

public readonly record struct PriceLevel(long PriceTicks, long Size);
