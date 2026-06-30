using FlowTerminal.Domain.Events;
using FlowTerminal.Domain.Instruments;
using FlowTerminal.MarketData.Abstractions;
using FlowTerminal.MarketData.Synthetic;
using FlowTerminal.Replay;
using FlowTerminal.Storage.Duck;
using FlowTerminal.Storage.Parquet;
using Xunit;

namespace FlowTerminal.IntegrationTests;

/// <summary>
/// Full Phase-5 path: synthetic feed → Parquet recording → DuckDB inspection →
/// deterministic ReplayValidator, exercising the same code the CLI tools call.
/// </summary>
public sealed class RecordInspectValidateTests : IDisposable
{
    private readonly string _dir;
    private static readonly Contract Nq = new(RootSymbol.NQ, QuarterlyMonth.December, 2025);
    private static readonly DateOnly Date = new(2024, 6, 3);
    private static readonly DateTime Start = new(2024, 6, 3, 14, 0, 0, DateTimeKind.Utc);

    public RecordInspectValidateTests() =>
        _dir = Path.Combine(Path.GetTempPath(), "ft-e2e-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Record_Inspect_And_Validate_Session()
    {
        var layout = new RecordingLayout(_dir);
        var events = new SyntheticSessionGenerator(1, Nq, Start, new SyntheticOptions { Seed = 21 })
            .Generate(6000).ToArray();

        await using (var recorder = new ParquetMarketDataRecorder(layout, RootSymbol.NQ, Nq.Symbol, Date, batchSize: 2000))
        {
            foreach (var e in events)
            {
                recorder.Record(e);
            }
        }

        var sessionDir = layout.SessionDirectory(RootSymbol.NQ, Nq.Symbol, Date);

        // DuckDB inspection sees every recorded event.
        Assert.Equal(events.Length, DuckDbInspector.CountEvents(sessionDir));
        var byType = DuckDbInspector.CountByType(sessionDir);
        Assert.Contains(byType, t => t.Type == (byte)MarketEventType.Trade && t.Count > 0);

        // Replay is deterministic across two passes.
        var source = new ParquetReplaySource(layout, RootSymbol.NQ, Nq.Symbol, Date);
        var report = await new ReplayValidator(source, new ReplayRequest(Nq, DateTime.MinValue, DateTime.MaxValue))
            .ValidateAsync();
        Assert.True(report.Match, report.Describe());
        Assert.Equal(0, source.CorruptParts);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }
}
