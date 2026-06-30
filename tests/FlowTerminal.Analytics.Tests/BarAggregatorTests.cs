using FlowTerminal.Analytics.Bars;
using FlowTerminal.Domain.Events;
using Xunit;
using static FlowTerminal.Analytics.Tests.TestTrades;

namespace FlowTerminal.Analytics.Tests;

public class BarAggregatorTests
{
    [Fact]
    public void Time_Bars_Close_On_Bucket_Boundary_With_Exact_Ohlc()
    {
        var agg = BarAggregator.Time(TimeSpan.FromMinutes(1));

        Assert.Null(agg.AddTrade(At(10, 100, 5, AggressorSide.Buy)));
        Assert.Null(agg.AddTrade(At(20, 102, 3, AggressorSide.Sell)));
        Assert.Null(agg.AddTrade(At(50, 101, 2, AggressorSide.Buy)));
        // Trade in the next minute closes the first bar.
        var completed = agg.AddTrade(At(65, 103, 4, AggressorSide.Buy));

        Assert.NotNull(completed);
        var bar = completed!.Value;
        Assert.Equal(100, bar.OpenTicks);
        Assert.Equal(102, bar.HighTicks);
        Assert.Equal(100, bar.LowTicks);
        Assert.Equal(101, bar.CloseTicks);
        Assert.Equal(10, bar.Volume);
        Assert.Equal(7, bar.BuyVolume);
        Assert.Equal(3, bar.SellVolume);
        Assert.Equal(4, bar.Delta);
        Assert.Equal(3, bar.TradeCount);
    }

    [Fact]
    public void Tick_Bars_Close_Every_N_Trades()
    {
        var agg = BarAggregator.Tick(2);
        Assert.Null(agg.AddTrade(At(0, 100, 1, AggressorSide.Buy)));
        var first = agg.AddTrade(At(1, 101, 1, AggressorSide.Sell));
        Assert.NotNull(first);
        Assert.Equal(2, first!.Value.TradeCount);

        Assert.Null(agg.AddTrade(At(2, 102, 1, AggressorSide.Buy)));
        var second = agg.AddTrade(At(3, 103, 1, AggressorSide.Buy));
        Assert.NotNull(second);
        Assert.Equal(102, second!.Value.OpenTicks);
        Assert.Equal(103, second.Value.CloseTicks);
    }

    [Fact]
    public void Volume_Bars_Close_When_Threshold_Reached()
    {
        var agg = BarAggregator.Volume(10);
        Assert.Null(agg.AddTrade(At(0, 100, 4, AggressorSide.Buy)));
        Assert.Null(agg.AddTrade(At(1, 100, 3, AggressorSide.Buy)));
        var bar = agg.AddTrade(At(2, 101, 5, AggressorSide.Sell)); // 12 >= 10
        Assert.NotNull(bar);
        Assert.Equal(12, bar!.Value.Volume);
    }

    [Fact]
    public void Range_Bars_Close_When_Span_Reaches_Range()
    {
        var agg = BarAggregator.Range(3);
        Assert.Null(agg.AddTrade(At(0, 100, 1, AggressorSide.Buy)));
        Assert.Null(agg.AddTrade(At(1, 101, 1, AggressorSide.Buy))); // span 1
        var bar = agg.AddTrade(At(2, 103, 1, AggressorSide.Buy));    // span 3
        Assert.NotNull(bar);
        Assert.Equal(3, bar!.Value.HighTicks - bar.Value.LowTicks);
    }

    [Fact]
    public void CloseCurrent_Emits_Developing_Bar()
    {
        var agg = BarAggregator.Time(TimeSpan.FromMinutes(5));
        agg.AddTrade(At(0, 100, 5, AggressorSide.Buy));
        var bar = agg.CloseCurrent();
        Assert.NotNull(bar);
        Assert.Equal(5, bar!.Value.Volume);
        Assert.Null(agg.CloseCurrent()); // nothing left
    }
}
