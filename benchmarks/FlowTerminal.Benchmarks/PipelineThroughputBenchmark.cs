using BenchmarkDotNet.Attributes;
using FlowTerminal.Domain.Events;
using FlowTerminal.Domain.Instruments;
using FlowTerminal.MarketData.Pipeline;
using FlowTerminal.MarketData.Synthetic;

namespace FlowTerminal.Benchmarks;

/// <summary>
/// Measures end-to-end canonical event throughput through a single
/// <see cref="InstrumentPipeline"/>. The product goal is ≥ 50,000 events/sec; this
/// benchmark produces the actual measured number rather than asserting a claim.
/// </summary>
[MemoryDiagnoser]
public class PipelineThroughputBenchmark
{
    private MarketEvent[] _events = Array.Empty<MarketEvent>();

    [Params(200_000)]
    public int EventCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var contract = new Contract(RootSymbol.NQ, QuarterlyMonth.December, 2025);
        var start = new DateTime(2024, 6, 3, 14, 0, 0, DateTimeKind.Utc);
        _events = new SyntheticSessionGenerator(1, contract, start, new SyntheticOptions { Seed = 1 })
            .Generate(EventCount).ToArray();
    }

    [Benchmark]
    public async Task<long> ProcessThroughPipeline()
    {
        var diag = new PipelineDiagnostics();
        long processed = 0;

        await using var pipeline = new InstrumentPipeline(
            instrumentId: 1,
            downstream: (_, _) => { Interlocked.Increment(ref processed); return ValueTask.CompletedTask; },
            diagnostics: diag,
            capacity: 1 << 16);
        pipeline.Start();

        foreach (var e in _events)
        {
            await pipeline.EnqueueAsync(e, CancellationToken.None);
        }

        pipeline.Complete();
        var spin = new SpinWait();
        while (Interlocked.Read(ref processed) < _events.Length)
        {
            spin.SpinOnce();
        }

        return processed;
    }
}
