using FlowTerminal.Analytics.Bars;
using FlowTerminal.Analytics.Detectors;
using FlowTerminal.Domain.Events;
using FlowTerminal.Domain.Instruments;
using FlowTerminal.MarketData.Synthetic;
using Xunit;

namespace FlowTerminal.Analytics.Tests;

/// <summary>
/// Every detector must be replay-safe: the same canonical stream yields the same
/// detections every time, so live and replay agree.
/// </summary>
public class DetectorDeterminismTests
{
    private static readonly Contract Nq = new(RootSymbol.NQ, QuarterlyMonth.December, 2025);
    private static readonly DateTime Start = new(2024, 6, 3, 14, 0, 0, DateTimeKind.Utc);

    private static (int large, int sweep, int absorption, int iceberg, int regime, int divergence) Run()
    {
        var gen = new SyntheticSessionGenerator(1, Nq, Start, new SyntheticOptions { Seed = 31, LargeTradeProbability = 0.05 });
        var large = new LargeTradeDetector();
        var sweep = new SweepDetector();
        var absorption = new AbsorptionDetector();
        var iceberg = new IcebergDetector();
        var regime = new MarketRegimeDetector();
        var divergence = new DeltaDivergenceDetector();
        var bars = BarAggregator.Time(TimeSpan.FromSeconds(15));

        int cl = 0, cs = 0, ca = 0, ci = 0, cr = 0, cd = 0;
        foreach (var e in gen.Generate(20_000))
        {
            if (iceberg.OnEvent(e) is not null) ci++;
            if (regime.OnEvent(e) is not null) cr++;
            if (e.Type == MarketEventType.Trade)
            {
                if (large.OnTrade(e) is not null) cl++;
                if (sweep.OnTrade(e) is not null) cs++;
                if (absorption.OnTrade(e) is not null) ca++;
                if (bars.AddTrade(e) is { } bar && divergence.OnBar(bar) is not null) cd++;
            }
        }

        return (cl, cs, ca, ci, cr, cd);
    }

    [Fact]
    public void All_Detectors_Are_Deterministic_Across_Runs()
    {
        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void Detectors_Produce_Some_Signals_On_Active_Data()
    {
        var r = Run();
        Assert.True(r.large > 0, "expected some large-trade detections");
        Assert.True(r.regime > 0, "expected at least one regime classification");
    }
}
