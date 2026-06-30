using FlowTerminal.Domain.Events;

namespace FlowTerminal.Analytics.BigTrades;

/// <summary>
/// How a trade's aggressor (taker) side was determined. Anything other than
/// <see cref="Native"/> is "estimated" and must be surfaced as such — the indicator
/// never presents an inferred side as exchange-confirmed fact.
/// </summary>
public enum ClassificationQuality : byte
{
    /// <summary>Exchange/provider supplied the aggressor side directly.</summary>
    Native = 0,

    /// <summary>Inferred from the trade's price versus the best bid/ask.</summary>
    InferredBidAsk = 1,

    /// <summary>Inferred from the tick rule (uptick = buy, downtick = sell) inside the spread.</summary>
    InferredTick = 2,

    /// <summary>Inherited from the most recent classified trade at the same price.</summary>
    Inherited = 3,

    /// <summary>Side could not be determined.</summary>
    Unknown = 4,

    /// <summary>Book was unreliable, so bid/ask inference was deliberately paused.</summary>
    InvalidBook = 5,
}

public static class ClassificationQualityExtensions
{
    /// <summary>True when the side was inferred rather than supplied by the feed.</summary>
    public static bool IsEstimated(this ClassificationQuality q) =>
        q is ClassificationQuality.InferredBidAsk or ClassificationQuality.InferredTick or ClassificationQuality.Inherited;
}

/// <summary>Result of classifying one trade's aggressor side.</summary>
public readonly record struct ClassifiedSide(AggressorSide Side, ClassificationQuality Quality)
{
    public bool IsEstimated => Quality.IsEstimated();
}

/// <summary>
/// A finalized Big Trade: either a single qualifying print or an aggregated group of
/// related aggressive executions. Purely observational — it describes executed
/// aggression, never asserts trader identity or intent. Prices are integer ticks.
/// </summary>
public sealed record BigTradeGroup(
    ulong Id,
    AggressorSide Side,
    ClassificationQuality Quality,
    DateTime StartUtc,
    DateTime EndUtc,
    long PriceTicks,          // placement price (weighted-average by default)
    long MinPriceTicks,
    long MaxPriceTicks,
    long TotalQuantity,
    int TradeCount,
    long LargestTrade,
    long ThresholdUsed,
    ThresholdMode ThresholdMode,
    double PercentileRank,    // 0..1 of the group total within the recent distribution (NaN if unknown)
    double ZScore,            // NaN when not applicable
    bool IsSweep,
    int SweepLevels,
    bool IsAggregated,
    long SequenceStart,
    long SequenceEnd)
{
    public TimeSpan Duration => EndUtc - StartUtc;

    public double AverageTradeSize => TradeCount > 0 ? TotalQuantity / (double)TradeCount : 0;

    /// <summary>True when the aggressor side was inferred rather than feed-supplied.</summary>
    public bool IsEstimated => Quality.IsEstimated();

    /// <summary>Number of distinct price levels the group executed across.</summary>
    public int PriceLevels => (int)((MaxPriceTicks - MinPriceTicks) + 1);
}
