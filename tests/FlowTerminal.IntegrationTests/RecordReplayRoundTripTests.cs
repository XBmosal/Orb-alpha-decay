using FlowTerminal.Domain.Events;
using FlowTerminal.Domain.Instruments;
using FlowTerminal.MarketData.Abstractions;
using FlowTerminal.MarketData.Pipeline;
using FlowTerminal.MarketData.Synthetic;
using FlowTerminal.Replay;
using Xunit;

namespace FlowTerminal.IntegrationTests;

/// <summary>
/// End-to-end Phase 1 integrity: synthetic provider → pipeline (with recording) →
/// in-memory replay produces an identical, reproducible event stream. This is the
/// determinism foundation that the full ReplayValidator (Phase 5) builds on.
/// </summary>
public class RecordReplayRoundTripTests
{
    private static readonly Contract Nq = new(RootSymbol.NQ, QuarterlyMonth.December, 2025);
    private static readonly DateTime Start = new(2024, 6, 3, 14, 0, 0, DateTimeKind.Utc);

    private sealed class CapturingRecorder : IMarketDataRecorder
    {
        private readonly List<MarketEvent> _events = new();
        private readonly object _lock = new();

        public bool IsRecording => true;
        public IReadOnlyList<MarketEvent> Events => _events;

        public void Record(in MarketEvent marketEvent)
        {
            lock (_lock)
            {
                _events.Add(marketEvent);
            }
        }

        public ValueTask FlushAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Recorded_Stream_Replays_Identically_Twice()
    {
        var recorder = new CapturingRecorder();
        var diag = new PipelineDiagnostics();

        await using (var pipeline = new InstrumentPipeline(
            instrumentId: 1, downstream: (_, _) => ValueTask.CompletedTask,
            diagnostics: diag, recorder: recorder))
        {
            pipeline.Start();
            var gen = new SyntheticSessionGenerator(1, Nq, Start, new SyntheticOptions { Seed = 42 });
            foreach (var e in gen.Generate(5000))
            {
                await pipeline.EnqueueAsync(e, CancellationToken.None);
            }

            pipeline.Complete();
            await WaitUntil(() => diag.EventsProcessed >= 5000, TimeSpan.FromSeconds(10));
        }

        var recorded = recorder.Events.ToArray();
        Assert.Equal(5000, recorded.Length);

        var source = new InMemoryReplaySource(recorded);
        var request = new ReplayRequest(Nq, Start.AddYears(-1), Start.AddYears(1));

        var firstHash = await HashReplay(source, request);
        var secondHash = await HashReplay(source, request);

        Assert.Equal(firstHash, secondHash);                 // replay is deterministic
        Assert.Equal(HashEvents(recorded), firstHash);       // replay matches the recording
    }

    private static async Task<ulong> HashReplay(IReplaySource source, ReplayRequest request)
    {
        ulong hash = 1469598103934665603UL; // FNV-1a offset
        await foreach (var e in source.ReadAsync(request, CancellationToken.None))
        {
            hash = Mix(hash, e);
        }

        return hash;
    }

    private static ulong HashEvents(IEnumerable<MarketEvent> events)
    {
        ulong hash = 1469598103934665603UL;
        foreach (var e in events)
        {
            hash = Mix(hash, e);
        }

        return hash;
    }

    private static ulong Mix(ulong hash, in MarketEvent e)
    {
        unchecked
        {
            hash = Fnv(hash, (ulong)e.Type);
            hash = Fnv(hash, (ulong)e.PriceTicks);
            hash = Fnv(hash, (ulong)e.Quantity);
            hash = Fnv(hash, (ulong)e.ExchangeSequence);
            hash = Fnv(hash, (ulong)e.ExchangeTimestampUtc.Ticks);
            hash = Fnv(hash, (ulong)e.Aggressor);
            return hash;
        }
    }

    private static ulong Fnv(ulong hash, ulong value)
    {
        unchecked
        {
            hash ^= value;
            return hash * 1099511628211UL;
        }
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
