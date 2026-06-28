using FlowTerminal.Domain.Events;
using FlowTerminal.Domain.Instruments;
using FlowTerminal.MarketData.Abstractions;
using FlowTerminal.MarketData.Synthetic;
using FlowTerminal.Storage.Parquet;
using Xunit;

namespace FlowTerminal.Replay.Tests;

public sealed class ReplayValidatorTests : IDisposable
{
    private readonly string _dir;
    private static readonly Contract Nq = new(RootSymbol.NQ, QuarterlyMonth.December, 2025);
    private static readonly DateOnly Date = new(2024, 6, 3);
    private static readonly DateTime Start = new(2024, 6, 3, 14, 0, 0, DateTimeKind.Utc);

    public ReplayValidatorTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ft-replayval-" + Guid.NewGuid().ToString("N"));
    }

    private static MarketEvent[] Generate(int count) =>
        new SyntheticSessionGenerator(1, Nq, Start, new SyntheticOptions { Seed = 13 }).Generate(count).ToArray();

    [Fact]
    public async Task InMemory_Replay_Is_Deterministic()
    {
        var source = new InMemoryReplaySource(Generate(5000));
        var validator = new ReplayValidator(source, new ReplayRequest(Nq, DateTime.MinValue, DateTime.MaxValue));
        var report = await validator.ValidateAsync();
        Assert.True(report.Match, report.Describe());
        Assert.True(report.First.TradeCount > 0);
    }

    [Fact]
    public async Task Parquet_Recorded_Session_Replays_Deterministically()
    {
        var layout = new RecordingLayout(_dir);
        await using (var recorder = new ParquetMarketDataRecorder(layout, RootSymbol.NQ, Nq.Symbol, Date, batchSize: 1000))
        {
            foreach (var e in Generate(4000))
            {
                recorder.Record(e);
            }
        }

        var source = new ParquetReplaySource(layout, RootSymbol.NQ, Nq.Symbol, Date);
        var validator = new ReplayValidator(source, new ReplayRequest(Nq, DateTime.MinValue, DateTime.MaxValue));
        var report = await validator.ValidateAsync();

        Assert.True(report.Match, report.Describe());
        // The reconstructed book/analytics produced a non-trivial state.
        Assert.True(report.First.EventCount >= 4000);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }
}
