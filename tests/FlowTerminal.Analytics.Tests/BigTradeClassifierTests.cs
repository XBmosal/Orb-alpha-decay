using FlowTerminal.Analytics.BigTrades;
using FlowTerminal.Domain.Events;
using FlowTerminal.Domain.Instruments;
using Xunit;

namespace FlowTerminal.Analytics.Tests;

public class BigTradeClassifierTests
{
    private static readonly DateTime T = new(2024, 6, 3, 14, 0, 0, DateTimeKind.Utc);

    private static MarketEvent Trade(long price, long qty, AggressorSide a = AggressorSide.Unknown,
        bool supplied = false, bool inferredFlag = false)
    {
        var flags = MarketEventFlags.None;
        if (supplied) flags |= MarketEventFlags.AggressorSupplied;
        if (inferredFlag) flags |= MarketEventFlags.AggressorInferred;
        return MarketEvent.Trade(1, RootSymbol.NQ, "NQZ5", "CME", T, T, price, qty, a, flags: flags);
    }

    [Fact]
    public void Provider_Side_Is_Native()
    {
        var c = new AggressorClassifier();
        var r = c.Classify(Trade(100, 5, AggressorSide.Buy, supplied: true), 99, 101, bookValid: true);
        Assert.Equal(AggressorSide.Buy, r.Side);
        Assert.Equal(ClassificationQuality.Native, r.Quality);
        Assert.False(r.IsEstimated);
    }

    [Fact]
    public void Trade_At_Or_Above_Ask_Is_Buy_Estimated()
    {
        var c = new AggressorClassifier();
        Assert.Equal(AggressorSide.Buy, c.Classify(Trade(101, 5), bestBidTicks: 99, bestAskTicks: 101, true).Side);
        var above = c.Classify(Trade(103, 5), 99, 101, true);
        Assert.Equal(AggressorSide.Buy, above.Side);
        Assert.Equal(ClassificationQuality.InferredBidAsk, above.Quality);
        Assert.True(above.IsEstimated);
    }

    [Fact]
    public void Trade_At_Or_Below_Bid_Is_Sell_Estimated()
    {
        var c = new AggressorClassifier();
        Assert.Equal(AggressorSide.Sell, c.Classify(Trade(99, 5), 99, 101, true).Side);
        Assert.Equal(AggressorSide.Sell, c.Classify(Trade(97, 5), 99, 101, true).Side);
    }

    [Fact]
    public void Inside_Spread_Uses_Tick_Rule()
    {
        var c = new AggressorClassifier();
        // Establish a last price inside the spread.
        c.Classify(Trade(100, 5), 98, 102, true);
        var up = c.Classify(Trade(101, 5), 98, 103, true);
        Assert.Equal(AggressorSide.Buy, up.Side);
        Assert.Equal(ClassificationQuality.InferredTick, up.Quality);
        var down = c.Classify(Trade(99, 5), 97, 103, true);
        Assert.Equal(AggressorSide.Sell, down.Side);
    }

    [Fact]
    public void Same_Price_Inside_Spread_Inherits_Last_Side()
    {
        var c = new AggressorClassifier();
        c.Classify(Trade(100, 5), 98, 102, true);       // no prior → unknown last
        c.Classify(Trade(101, 5), 98, 103, true);       // buy (uptick)
        var same = c.Classify(Trade(101, 5), 98, 103, true); // same price → inherit buy
        Assert.Equal(AggressorSide.Buy, same.Side);
        Assert.Equal(ClassificationQuality.Inherited, same.Quality);
    }

    [Fact]
    public void Invalid_Book_Pauses_Inference()
    {
        var c = new AggressorClassifier();
        var r = c.Classify(Trade(100, 5), 99, 101, bookValid: false);
        Assert.Equal(AggressorSide.Unknown, r.Side);
        Assert.Equal(ClassificationQuality.InvalidBook, r.Quality);
    }

    [Fact]
    public void Invalid_Book_Still_Honours_Provider_Side()
    {
        var c = new AggressorClassifier();
        var r = c.Classify(Trade(100, 5, AggressorSide.Sell, supplied: true), 99, 101, bookValid: false);
        Assert.Equal(AggressorSide.Sell, r.Side);
        Assert.Equal(ClassificationQuality.Native, r.Quality);
    }

    [Fact]
    public void Missing_Context_Is_Unknown_Not_Forced()
    {
        var c = new AggressorClassifier();
        // No provider side, no prior trade, price strictly inside spread, both touches known.
        var r = c.Classify(Trade(100, 5), bestBidTicks: 98, bestAskTicks: 102, true);
        Assert.Equal(AggressorSide.Unknown, r.Side);
        Assert.Equal(ClassificationQuality.Unknown, r.Quality);
    }

    [Fact]
    public void Feed_Flagged_Inferred_Is_Estimated_Not_Native()
    {
        var c = new AggressorClassifier();
        var r = c.Classify(Trade(100, 5, AggressorSide.Buy, supplied: true, inferredFlag: true), 99, 101, true);
        Assert.Equal(AggressorSide.Buy, r.Side);
        Assert.True(r.Quality.IsEstimated());
    }
}
