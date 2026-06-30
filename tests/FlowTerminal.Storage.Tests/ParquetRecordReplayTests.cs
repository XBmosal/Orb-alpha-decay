using FlowTerminal.Domain.Events;
using FlowTerminal.Domain.Instruments;
using FlowTerminal.MarketData.Abstractions;
using FlowTerminal.MarketData.Synthetic;
using FlowTerminal.Storage.Parquet;
using Xunit;

namespace FlowTerminal.Storage.Tests;

public sealed class ParquetRecordReplayTests : IDisposable
{
    private readonly string _dir;
    private readonly RecordingLayout _layout;
    private static readonly Contract Nq = new(RootSymbol.NQ, QuarterlyMonth.December, 2025);
    private static readonly DateOnly Date = new(2024, 6, 3);
    private static readonly DateTime Start = new(2024, 6, 3, 14, 0, 0, DateTimeKind.Utc);

    public ParquetRecordReplayTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ft-parquet-" + Guid.NewGuid().ToString("N"));
        _layout = new RecordingLayout(_dir);
    }

    private static MarketEvent[] Generate(int count) =>
        new SyntheticSessionGenerator(1, Nq, Start, new SyntheticOptions { Seed = 5 }).Generate(count).ToArray();

    private static ulong Hash(IEnumerable<MarketEvent> events)
    {
        ulong h = 1469598103934665603UL;
        foreach (var e in events)
        {
            unchecked
            {
                h = (h ^ (ulong)e.Type) * 1099511628211UL;
                h = (h ^ (ulong)e.PriceTicks) * 1099511628211UL;
                h = (h ^ (ulong)e.Quantity) * 1099511628211UL;
                h = (h ^ (ulong)e.ExchangeSequence) * 1099511628211UL;
                h = (h ^ (ulong)e.ExchangeTimestampUtc.Ticks) * 1099511628211UL;
            }
        }

        return h;
    }

    [Fact]
    public async Task Record_Then_Replay_Reproduces_Events_In_Order()
    {
        var events = Generate(2500);

        await using (var recorder = new ParquetMarketDataRecorder(_layout, RootSymbol.NQ, Nq.Symbol, Date, batchSize: 1000))
        {
            foreach (var e in events)
            {
                recorder.Record(e);
            }
        }

        // Multiple part files should have been produced (2500 / 1000).
        var parts = _layout.EnumerateParts(RootSymbol.NQ, Nq.Symbol, Date);
        Assert.True(parts.Count >= 3, $"expected >= 3 parts, got {parts.Count}");

        var source = new ParquetReplaySource(_layout, RootSymbol.NQ, Nq.Symbol, Date);
        var replayed = new List<MarketEvent>();
        await foreach (var e in source.ReadAllAsync(default))
        {
            replayed.Add(e);
        }

        Assert.Equal(events.Length, replayed.Count);
        Assert.Equal(Hash(events), Hash(replayed));
        Assert.Equal(0, source.CorruptParts);
    }

    [Fact]
    public async Task Time_Filtered_Replay_Respects_Range()
    {
        var events = Generate(1000);
        await using (var recorder = new ParquetMarketDataRecorder(_layout, RootSymbol.NQ, Nq.Symbol, Date, batchSize: 500))
        {
            foreach (var e in events)
            {
                recorder.Record(e);
            }
        }

        var cutoff = events[500].ExchangeTimestampUtc;
        var source = new ParquetReplaySource(_layout, RootSymbol.NQ, Nq.Symbol, Date);
        var count = 0;
        await foreach (var e in source.ReadAsync(new ReplayRequest(Nq, cutoff, DateTime.MaxValue), default))
        {
            Assert.True(e.ExchangeTimestampUtc >= cutoff);
            count++;
        }

        Assert.True(count > 0 && count < events.Length);
    }

    [Fact]
    public async Task Corrupt_Part_Is_Skipped_Not_Fatal()
    {
        var events = Generate(3000);
        await using (var recorder = new ParquetMarketDataRecorder(_layout, RootSymbol.NQ, Nq.Symbol, Date, batchSize: 1000))
        {
            foreach (var e in events)
            {
                recorder.Record(e);
            }
        }

        var parts = _layout.EnumerateParts(RootSymbol.NQ, Nq.Symbol, Date);
        Assert.True(parts.Count >= 3);

        // Corrupt the middle part by truncating it (simulates an interrupted write).
        File.WriteAllBytes(parts[1], new byte[] { 0x50, 0x41, 0x52, 0x31, 0x00 }); // "PAR1" + junk

        var source = new ParquetReplaySource(_layout, RootSymbol.NQ, Nq.Symbol, Date);
        var replayed = new List<MarketEvent>();
        await foreach (var e in source.ReadAllAsync(default))
        {
            replayed.Add(e);
        }

        Assert.Equal(1, source.CorruptParts);          // detected, not fatal
        Assert.True(replayed.Count > 0);                // surviving parts still replay
        Assert.True(replayed.Count < events.Length);    // the corrupt part's events are absent
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }
}
