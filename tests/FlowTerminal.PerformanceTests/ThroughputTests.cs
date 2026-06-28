using System.Diagnostics;
using FlowTerminal.Domain.Instruments;
using FlowTerminal.MarketData.Pipeline;
using FlowTerminal.MarketData.Synthetic;
using Xunit;
using Xunit.Abstractions;

namespace FlowTerminal.PerformanceTests;

/// <summary>
/// Performance goal verification: the pipeline must sustain at least 50,000
/// normalized events/second while dropping zero canonical events. This is a
/// measurable assertion, not a claim — the measured rate is emitted to test output.
/// </summary>
public class ThroughputTests
{
    private const int TargetEventsPerSecond = 50_000;
    private readonly ITestOutputHelper _output;

    public ThroughputTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Pipeline_Sustains_At_Least_50k_Events_Per_Second()
    {
        var contract = new Contract(RootSymbol.NQ, QuarterlyMonth.December, 2025);
        var start = new DateTime(2024, 6, 3, 14, 0, 0, DateTimeKind.Utc);
        var diag = new PipelineDiagnostics();
        long processed = 0;

        await using var pipeline = new InstrumentPipeline(
            instrumentId: 1,
            downstream: (_, _) => { Interlocked.Increment(ref processed); return ValueTask.CompletedTask; },
            diagnostics: diag,
            capacity: 1 << 16);
        pipeline.Start();

        const int total = 500_000;
        // Pre-generate to isolate pipeline throughput from generation cost.
        var events = new SyntheticSessionGenerator(1, contract, start, new SyntheticOptions { Seed = 1 })
            .Generate(total).ToArray();

        var sw = Stopwatch.StartNew();
        foreach (var e in events)
        {
            await pipeline.EnqueueAsync(e, CancellationToken.None);
        }

        pipeline.Complete();
        var spin = new SpinWait();
        while (Interlocked.Read(ref processed) < total && sw.Elapsed < TimeSpan.FromSeconds(60))
        {
            spin.SpinOnce();
        }

        sw.Stop();

        double eventsPerSecond = total / sw.Elapsed.TotalSeconds;
        _output.WriteLine($"Processed {total:N0} events in {sw.Elapsed.TotalMilliseconds:N1} ms " +
                          $"= {eventsPerSecond:N0} events/sec");
        _output.WriteLine($"Max queue depth: {diag.MaxQueueDepth:N0}, dropped canonical: {diag.DroppedCanonicalEvents}");

        Assert.Equal(0, diag.DroppedCanonicalEvents);
        Assert.Equal(total, processed);
        Assert.True(eventsPerSecond >= TargetEventsPerSecond,
            $"Throughput {eventsPerSecond:N0}/s below target {TargetEventsPerSecond:N0}/s.");
    }
}
