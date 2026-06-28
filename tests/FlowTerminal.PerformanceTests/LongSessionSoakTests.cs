using FlowTerminal.Analytics.Bars;
using FlowTerminal.Analytics.Delta;
using FlowTerminal.Analytics.Profiles;
using FlowTerminal.Domain.Events;
using FlowTerminal.Domain.Instruments;
using FlowTerminal.MarketData.Pipeline;
using FlowTerminal.MarketData.Synthetic;
using FlowTerminal.OrderBook;
using Xunit;
using Xunit.Abstractions;

namespace FlowTerminal.PerformanceTests;

/// <summary>
/// Compressed long-session soak: drives a large event count through the full
/// pipeline + book + analytics and checks that memory stays bounded (no unbounded
/// queue/cache growth) and that zero canonical events are dropped — the stability
/// goals for an 8-hour session, run in seconds.
/// </summary>
public class LongSessionSoakTests
{
    private readonly ITestOutputHelper _output;
    public LongSessionSoakTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Sustained_Load_Keeps_Memory_Bounded_And_Drops_Zero()
    {
        var contract = new Contract(RootSymbol.NQ, QuarterlyMonth.December, 2025);
        var start = new DateTime(2024, 6, 3, 14, 0, 0, DateTimeKind.Utc);
        var diag = new PipelineDiagnostics();

        var book = new MarketByPriceOrderBook();
        var cvd = new CvdCalculator();
        var profile = new VolumeProfile();
        var bars = BarAggregator.Time(TimeSpan.FromMinutes(1));
        int barCount = 0;
        long processed = 0;

        await using var pipeline = new InstrumentPipeline(
            instrumentId: 1,
            downstream: (e, _) =>
            {
                book.Apply(e);
                if (e.Type == MarketEventType.Trade)
                {
                    cvd.Add(e);
                    profile.AddTrade(e);
                    if (bars.AddTrade(e) is not null) barCount++;
                }

                Interlocked.Increment(ref processed);
                return ValueTask.CompletedTask;
            },
            diagnostics: diag,
            capacity: 1 << 16);
        pipeline.Start();

        const int total = 3_000_000;
        var gen = new SyntheticSessionGenerator(1, contract, start, new SyntheticOptions { Seed = 1 });
        long memMid = 0;

        for (int i = 0; i < total; i++)
        {
            await pipeline.EnqueueAsync(gen.Next(), CancellationToken.None);
            if (i == total / 2)
            {
                while (Interlocked.Read(ref processed) < i) { var s = new SpinWait(); s.SpinOnce(); }
                memMid = SampleManagedMemory();
            }
        }

        pipeline.Complete();
        var spin = new SpinWait();
        while (Interlocked.Read(ref processed) < total) spin.SpinOnce();

        long memEnd = SampleManagedMemory();
        _output.WriteLine($"processed={processed:N0} bars={barCount} memMid={memMid / 1_000_000}MB memEnd={memEnd / 1_000_000}MB maxQueue={diag.MaxQueueDepth}");

        Assert.Equal(total, processed);
        Assert.Equal(0, diag.DroppedCanonicalEvents);            // never drop canonical events
        Assert.True(diag.MaxQueueDepth <= (1 << 16));            // bounded queue
        Assert.True(memEnd < 512_000_000, $"managed memory {memEnd / 1_000_000}MB exceeded 512MB");
        // No unbounded growth: second-half memory must not balloon vs the midpoint.
        Assert.True(memEnd - memMid < 128_000_000, $"memory grew {(memEnd - memMid) / 1_000_000}MB in the second half");
    }

    private static long SampleManagedMemory()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        return GC.GetTotalMemory(forceFullCollection: true);
    }
}
