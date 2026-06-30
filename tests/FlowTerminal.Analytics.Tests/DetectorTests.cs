using FlowTerminal.Analytics.Bars;
using FlowTerminal.Analytics.Detectors;
using FlowTerminal.Domain.Events;
using FlowTerminal.Domain.Instruments;
using Xunit;
using static FlowTerminal.Analytics.Tests.TestTrades;

namespace FlowTerminal.Analytics.Tests;

public class LargeTradeDetectorTests
{
    [Fact]
    public void Flags_Trades_At_Or_Above_Fixed_Threshold()
    {
        var d = new LargeTradeDetector(new LargeTradeSettings { FixedThreshold = 50, UseRollingPercentile = false });
        Assert.Null(d.OnTrade(At(0, 100, 10, AggressorSide.Buy)));
        var hit = d.OnTrade(At(1, 100, 80, AggressorSide.Buy));
        Assert.NotNull(hit);
        Assert.Equal(DetectionBias.Bullish, hit!.Bias);
        Assert.False(hit.IsEstimated);
        Assert.Equal(80, hit.Measurements["size"]);
    }

    [Fact]
    public void Disabled_Detector_Emits_Nothing()
    {
        var d = new LargeTradeDetector(new LargeTradeSettings { FixedThreshold = 1, UseRollingPercentile = false }) { Enabled = false };
        Assert.Null(d.OnTrade(At(0, 100, 999, AggressorSide.Buy)));
    }

    [Fact]
    public void Es_Default_Threshold_Is_Higher_Than_Nq()
    {
        Assert.True(LargeTradeSettings.ForEs().FixedThreshold > LargeTradeSettings.ForNq().FixedThreshold);
    }
}

public class SweepDetectorTests
{
    [Fact]
    public void Detects_Upward_Sweep_Across_Levels()
    {
        var d = new SweepDetector(new SweepSettings { MinLevels = 3, MinVolume = 10, MaxGap = TimeSpan.FromSeconds(1) });
        Assert.Null(d.OnTrade(At(0, 100, 5, AggressorSide.Buy)));
        Assert.Null(d.OnTrade(At(0, 101, 5, AggressorSide.Buy)));
        var hit = d.OnTrade(At(0, 102, 5, AggressorSide.Buy)); // 3 levels, vol 15
        Assert.NotNull(hit);
        Assert.Equal(DetectionBias.Bullish, hit!.Bias);
        Assert.Equal(3, hit.Measurements["levels"]);
    }

    [Fact]
    public void Time_Gap_Breaks_The_Run()
    {
        var d = new SweepDetector(new SweepSettings { MinLevels = 3, MinVolume = 1, MaxGap = TimeSpan.FromMilliseconds(100) });
        d.OnTrade(At(0, 100, 5, AggressorSide.Buy));
        d.OnTrade(At(1, 101, 5, AggressorSide.Buy)); // 1s gap > 100ms → resets
        Assert.Null(d.OnTrade(At(2, 102, 5, AggressorSide.Buy)));
    }
}

public class AbsorptionDetectorTests
{
    [Fact]
    public void Detects_Volume_Absorbed_In_Tight_Band()
    {
        var d = new AbsorptionDetector(new AbsorptionSettings { MinVolume = 100, MaxMoveTicks = 1, Window = TimeSpan.FromSeconds(5) });
        Detection? hit = null;
        for (int i = 0; i < 10; i++)
        {
            // 150 total within 1 tick, packed 200ms apart so it fits the 5s window.
            hit ??= d.OnTrade(Trade(Base.AddMilliseconds(i * 200), 100, 15, AggressorSide.Buy));
        }

        Assert.NotNull(hit);
        Assert.Equal(DetectionBias.Bearish, hit!.Bias); // buyers absorbed by resting offers
        Assert.True(hit.Measurements["totalVolume"] >= 100);
    }

    [Fact]
    public void Price_Moving_Away_Prevents_Absorption()
    {
        var d = new AbsorptionDetector(new AbsorptionSettings { MinVolume = 50, MaxMoveTicks = 1 });
        Detection? hit = null;
        // Each print is 2 ticks from the last (beyond MaxMoveTicks=1) → never accumulates.
        for (int i = 0; i < 6; i++) hit ??= d.OnTrade(Trade(Base.AddMilliseconds(i * 100), 100 + i * 2, 30, AggressorSide.Buy));
        Assert.Null(hit);
    }
}

public class IcebergDetectorTests
{
    private static MarketEvent Depth(long price, long size) =>
        MarketEvent.Quote(1, RootSymbol.NQ, "NQZ5", "CME Globex", MarketEventType.AskUpdate, Base, Base, Side.Ask, price, size);

    [Fact]
    public void Estimated_Iceberg_When_Executed_Exceeds_Displayed()
    {
        var d = new IcebergDetector(new IcebergSettings { RefillRatio = 4, MinExecuted = 100 });
        d.OnEvent(Depth(102, 20)); // displayed only 20
        Detection? hit = null;
        for (int i = 0; i < 10; i++) hit ??= d.OnEvent(At(i, 102, 15, AggressorSide.Buy)); // executes 150 >> 20
        Assert.NotNull(hit);
        Assert.True(hit!.IsEstimated);                  // never claimed certain
        Assert.Contains("Estimated", hit.Label);
        Assert.True(hit.Measurements["ratio"] >= 4);
    }
}

public class ReplenishmentDetectorTests
{
    private static MarketEvent Bid(long price, long size, int sec) =>
        MarketEvent.Quote(1, RootSymbol.NQ, "NQZ5", "CME Globex", MarketEventType.BidUpdate, Base.AddSeconds(sec), Base.AddSeconds(sec), Side.Bid, price, size);

    [Fact]
    public void Detects_Repeated_Replenishment()
    {
        var d = new ReplenishmentDetector(new ReplenishmentSettings { MinReplenishments = 3, MinSize = 20, Window = TimeSpan.FromSeconds(30) });
        Detection? hit = null;
        hit ??= d.OnDepth(Bid(100, 50, 0));
        for (int i = 0; i < 3; i++)
        {
            hit ??= d.OnDepth(Bid(100, 0, i * 2 + 1));   // deplete
            hit ??= d.OnDepth(Bid(100, 40, i * 2 + 2));  // replenish
        }

        Assert.NotNull(hit);
        Assert.Equal(DetectionBias.Bullish, hit!.Bias);
    }
}

public class StopRunDetectorTests
{
    [Fact]
    public void Detects_Breakout_Through_Swing_High_With_Volume_Burst()
    {
        var d = new StopRunDetector(new StopRunSettings
        {
            Lookback = TimeSpan.FromMinutes(5),
            ShortWindow = TimeSpan.FromSeconds(3),
            VolumeBurstFactor = 1.5,
            BreakoutBufferTicks = 1,
            Cooldown = TimeSpan.FromSeconds(1),
        });

        // Build a base range 100..105 with small volume.
        for (int i = 0; i < 20; i++) d.OnTrade(At(i, 100 + (i % 6), 2, AggressorSide.Buy));
        // Breakout above 105 with a volume burst.
        var hit = d.OnTrade(At(40, 110, 80, AggressorSide.Buy));
        Assert.NotNull(hit);
        Assert.Equal(DetectionBias.Bullish, hit!.Bias);
    }
}

public class MarketRegimeDetectorTests
{
    [Fact]
    public void Transitions_From_Quiet_To_Faster_Regime()
    {
        var d = new MarketRegimeDetector(new MarketRegimeSettings { Window = TimeSpan.FromSeconds(10), QuietMaxTps = 2, NormalMaxTps = 10, FastMaxTps = 30 });
        // Many trades within a 1s span → high trades/sec → leaves Quiet.
        Detection? change = null;
        for (int i = 0; i < 60; i++)
        {
            var e = Trade(Base.AddMilliseconds(i * 10), 100, 1, AggressorSide.Buy);
            change ??= d.OnEvent(e);
        }

        Assert.NotNull(change);
        Assert.NotEqual(MarketRegime.Quiet, d.Current);
    }
}

public class DeltaDivergenceDetectorTests
{
    private static Bar Bar(long high, long low, long delta) =>
        new(BarKind.Time, Base, Base, low, high, low, high, 100, 50 + delta / 2, 50 - delta / 2, 10);

    [Fact]
    public void Bearish_Divergence_On_Higher_High_Weaker_Delta()
    {
        var d = new DeltaDivergenceDetector(new DeltaDivergenceSettings { Lookback = 3 });
        Assert.Null(d.OnBar(Bar(100, 90, 40)));  // swing high with strong delta
        Assert.Null(d.OnBar(Bar(98, 88, 10)));
        Assert.Null(d.OnBar(Bar(99, 89, 10)));
        Assert.Null(d.OnBar(Bar(99, 89, 10)));
        var hit = d.OnBar(Bar(105, 95, 8));      // higher high, weaker delta
        Assert.NotNull(hit);
        Assert.Equal(DetectionBias.Bearish, hit!.Bias);
    }
}
