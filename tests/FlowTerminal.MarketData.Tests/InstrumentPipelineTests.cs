using FlowTerminal.Domain.Events;
using FlowTerminal.Domain.Instruments;
using FlowTerminal.MarketData.Pipeline;
using FlowTerminal.MarketData.Synthetic;
using Xunit;

namespace FlowTerminal.MarketData.Tests;

public class InstrumentPipelineTests
{
    private static readonly Contract Nq = new(RootSymbol.NQ, QuarterlyMonth.December, 2025);
    private static readonly DateTime Start = new(2024, 6, 3, 14, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task All_Events_Flow_Through_Without_Drops()
    {
        var diag = new PipelineDiagnostics();
        long processed = 0;

        await using var pipeline = new InstrumentPipeline(
            instrumentId: 1,
            downstream: (_, _) => { Interlocked.Increment(ref processed); return ValueTask.CompletedTask; },
            diagnostics: diag);
        pipeline.Start();

        var gen = new SyntheticSessionGenerator(1, Nq, Start, new SyntheticOptions { Seed = 1 });
        const int count = 10_000;
        foreach (var e in gen.Generate(count))
        {
            await pipeline.EnqueueAsync(e, CancellationToken.None);
        }

        pipeline.Complete();
        await WaitUntil(() => Interlocked.Read(ref processed) >= count, TimeSpan.FromSeconds(10));

        Assert.Equal(count, diag.EventsIngested);
        Assert.Equal(0, diag.DroppedCanonicalEvents); // cardinal invariant
        Assert.True(Interlocked.Read(ref processed) >= count);
    }

    [Fact]
    public async Task Sequence_Gap_Emits_Downstream_SequenceGap_Event()
    {
        var diag = new PipelineDiagnostics();
        int gapEvents = 0;
        var done = new TaskCompletionSource();

        await using var pipeline = new InstrumentPipeline(
            instrumentId: 1,
            downstream: (ev, _) =>
            {
                if (ev.Type == MarketEventType.SequenceGap)
                {
                    Interlocked.Increment(ref gapEvents);
                    done.TrySetResult();
                }

                return ValueTask.CompletedTask;
            },
            diagnostics: diag);
        pipeline.Start();

        var gen = new SyntheticSessionGenerator(1, Nq, Start, new SyntheticOptions { Seed = 1, InjectGapAfter = 20 });
        foreach (var e in gen.Generate(60))
        {
            await pipeline.EnqueueAsync(e, CancellationToken.None);
        }

        pipeline.Complete();
        await Task.WhenAny(done.Task, Task.Delay(TimeSpan.FromSeconds(10)));

        Assert.True(gapEvents >= 1);
        Assert.True(diag.SequenceGaps >= 1);
        Assert.True(diag.InvalidationCount >= 1);
        Assert.Equal(0, diag.DroppedCanonicalEvents);
    }

    [Fact]
    public async Task Duplicate_Events_Are_Counted_Not_Forwarded()
    {
        var diag = new PipelineDiagnostics();
        int forwarded = 0;

        await using var pipeline = new InstrumentPipeline(
            instrumentId: 1,
            downstream: (_, _) => { Interlocked.Increment(ref forwarded); return ValueTask.CompletedTask; },
            diagnostics: diag);
        pipeline.Start();

        MarketEvent Make(long seq) => MarketEvent.Quote(
            1, RootSymbol.NQ, "NQZ5", "CME Globex", MarketEventType.BidUpdate,
            Start, Start, Side.Bid, 100, 1, exchangeSequence: seq,
            flags: MarketEventFlags.HasExchangeSequence);

        await pipeline.EnqueueAsync(Make(1), CancellationToken.None);
        await pipeline.EnqueueAsync(Make(2), CancellationToken.None);
        await pipeline.EnqueueAsync(Make(2), CancellationToken.None); // duplicate

        pipeline.Complete();
        await WaitUntil(() => diag.DuplicateEvents >= 1, TimeSpan.FromSeconds(10));

        Assert.Equal(1, diag.DuplicateEvents);
        Assert.Equal(2, forwarded); // the duplicate is not forwarded
        Assert.Equal(0, diag.DroppedCanonicalEvents);
    }

    private static async Task WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException("Condition not met within timeout.");
    }
}
