using FlowTerminal.Domain.Events;

namespace FlowTerminal.Analytics.BigTrades;

/// <summary>
/// Determines a trade's aggressor (taker) side, preferring the feed-supplied value and
/// falling back to a documented, deterministic hierarchy only when it is missing:
///
///   1. Provider-supplied side                       → Native.
///   2. Price ≥ best ask                              → Buy  (estimated, bid/ask).
///   3. Price ≤ best bid                              → Sell (estimated, bid/ask).
///   4. Inside the spread → tick rule (up/down)       → estimated, tick.
///   5. Unchanged price → inherit last classified side → estimated, inherited.
///   6. Otherwise                                     → Unknown.
///
/// When the order book is unreliable, bid/ask inference is paused: a provider side is
/// still honoured, otherwise the trade is Unknown (never silently guessed). The class
/// is single-writer and deterministic, so replay reproduces identical classifications.
/// </summary>
public sealed class AggressorClassifier
{
    private long _lastPriceTicks = long.MinValue;
    private AggressorSide _lastSide = AggressorSide.Unknown;

    public void Reset()
    {
        _lastPriceTicks = long.MinValue;
        _lastSide = AggressorSide.Unknown;
    }

    /// <summary>
    /// Classifies <paramref name="trade"/> against the current best bid/ask (in ticks).
    /// Pass <paramref name="bookValid"/> = false to pause bid/ask inference when the book
    /// is in an unreliable state. <see cref="BookSideNone"/> marks an unknown best price.
    /// </summary>
    public ClassifiedSide Classify(in MarketEvent trade, long bestBidTicks, long bestAskTicks, bool bookValid)
    {
        long prevPrice = _lastPriceTicks;
        var result = ClassifyCore(trade, bestBidTicks, bestAskTicks, bookValid, prevPrice);

        // Always seed the tick-rule reference with this trade's price (even when its side is
        // unknown), so the next inside-spread trade has something to compare against. Keep the
        // last *side* only when we actually determined one.
        _lastPriceTicks = trade.PriceTicks;
        if (result.Side != AggressorSide.Unknown) _lastSide = result.Side;
        return result;
    }

    private ClassifiedSide ClassifyCore(in MarketEvent trade, long bestBidTicks, long bestAskTicks, bool bookValid, long prevPrice)
    {
        // 1. Provider-supplied side is authoritative (unless the feed itself flagged it inferred).
        if (trade.Aggressor != AggressorSide.Unknown && !trade.HasFlag(MarketEventFlags.AggressorInferred))
            return new ClassifiedSide(trade.Aggressor, ClassificationQuality.Native);

        // A feed value that was explicitly flagged inferred is honoured but marked estimated.
        if (trade.Aggressor != AggressorSide.Unknown)
            return new ClassifiedSide(trade.Aggressor, ClassificationQuality.InferredBidAsk);

        // No provider side: inference requires a trustworthy book.
        if (!bookValid)
            return new ClassifiedSide(AggressorSide.Unknown, ClassificationQuality.InvalidBook);

        bool haveBid = bestBidTicks != BookSideNone;
        bool haveAsk = bestAskTicks != BookSideNone;

        // 2/3. At or through a touch.
        if (haveAsk && trade.PriceTicks >= bestAskTicks)
            return new ClassifiedSide(AggressorSide.Buy, ClassificationQuality.InferredBidAsk);
        if (haveBid && trade.PriceTicks <= bestBidTicks)
            return new ClassifiedSide(AggressorSide.Sell, ClassificationQuality.InferredBidAsk);

        // 4. Inside the spread → tick rule against the previous trade price.
        if (prevPrice != long.MinValue && trade.PriceTicks != prevPrice)
        {
            var side = trade.PriceTicks > prevPrice ? AggressorSide.Buy : AggressorSide.Sell;
            return new ClassifiedSide(side, ClassificationQuality.InferredTick);
        }

        // 5. Same price as the previous trade → inherit, where we have a prior side.
        if (_lastSide != AggressorSide.Unknown)
            return new ClassifiedSide(_lastSide, ClassificationQuality.Inherited);

        // 6. Nothing to go on.
        return new ClassifiedSide(AggressorSide.Unknown, ClassificationQuality.Unknown);
    }

    /// <summary>Sentinel for "no established best price" (mirrors the order book's NoPrice).</summary>
    public const long BookSideNone = long.MinValue;
}
