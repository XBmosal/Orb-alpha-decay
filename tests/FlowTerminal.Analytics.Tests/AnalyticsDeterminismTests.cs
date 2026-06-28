using FlowTerminal.Analytics.Bars;
using FlowTerminal.Analytics.Delta;
using FlowTerminal.Analytics.Profiles;
using FlowTerminal.Analytics.Vwap;
using FlowTerminal.Domain.Events;
using FlowTerminal.Domain.Instruments;
using FlowTerminal.MarketData.Synthetic;
using Xunit;

namespace FlowTerminal.Analytics.Tests;

/// <summary>
/// The same canonical trade stream must produce identical analytics every time —
/// this is what guarantees live and replay (which share this exact code) agree.
/// </summary>
public class AnalyticsDeterminismTests
{
    private static readonly Contract Nq = new(RootSymbol.NQ, QuarterlyMonth.December, 2025);
    private static readonly DateTime Start = new(2024, 6, 3, 14, 0, 0, DateTimeKind.Utc);

    private static (long cvd, long poc, double vwap, int bars) Run()
    {
        var gen = new SyntheticSessionGenerator(1, Nq, Start, new SyntheticOptions { Seed = 99 });
        var cvd = new CvdCalculator();
        var profile = new VolumeProfile();
        var vwap = new VwapCalculator();
        var bars = BarAggregator.Time(TimeSpan.FromSeconds(15));
        int barCount = 0;

        foreach (var e in gen.Generate(10_000))
        {
            if (e.Type != MarketEventType.Trade)
            {
                continue;
            }

            cvd.Add(e);
            profile.AddTrade(e);
            vwap.Add(e);
            if (bars.AddTrade(e) is not null)
            {
                barCount++;
            }
        }

        return (cvd.CumulativeDelta, profile.PocTicks(), vwap.Value().VwapTicks, barCount);
    }

    [Fact]
    public void Repeated_Runs_Produce_Identical_Analytics()
    {
        var a = Run();
        var b = Run();
        Assert.Equal(a, b);
    }
}
