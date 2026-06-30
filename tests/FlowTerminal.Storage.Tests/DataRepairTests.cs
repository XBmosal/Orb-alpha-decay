using FlowTerminal.Domain.Instruments;
using FlowTerminal.MarketData.Synthetic;
using FlowTerminal.Storage.Parquet;
using FlowTerminal.Storage.Repair;
using Xunit;

namespace FlowTerminal.Storage.Tests;

public sealed class DataRepairTests : IDisposable
{
    private readonly string _dir;
    private readonly RecordingLayout _layout;
    private static readonly Contract Nq = new(RootSymbol.NQ, QuarterlyMonth.December, 2025);
    private static readonly DateOnly Date = new(2024, 6, 3);
    private static readonly DateTime Start = new(2024, 6, 3, 14, 0, 0, DateTimeKind.Utc);

    public DataRepairTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ft-repair-" + Guid.NewGuid().ToString("N"));
        _layout = new RecordingLayout(_dir);
    }

    private async Task RecordAsync(int count, int batch)
    {
        await using var recorder = new ParquetMarketDataRecorder(_layout, RootSymbol.NQ, Nq.Symbol, Date, batchSize: batch);
        foreach (var e in new SyntheticSessionGenerator(1, Nq, Start, new SyntheticOptions { Seed = 4 }).Generate(count))
        {
            recorder.Record(e);
        }
    }

    [Fact]
    public async Task Healthy_Session_Reports_No_Issues()
    {
        await RecordAsync(3000, 1000);
        var report = await new DataRepairService().ScanAsync(_layout.SessionDirectory(RootSymbol.NQ, Nq.Symbol, Date), quarantine: false);
        Assert.True(report.IsHealthy);
        Assert.Equal(report.TotalParts, report.ValidParts);
        Assert.Equal(0, report.CorruptParts);
    }

    [Fact]
    public async Task Corrupt_Part_And_Orphan_Temp_Are_Detected_And_Quarantined()
    {
        await RecordAsync(3000, 1000);
        var sessionDir = _layout.SessionDirectory(RootSymbol.NQ, Nq.Symbol, Date);
        var parts = _layout.EnumerateParts(RootSymbol.NQ, Nq.Symbol, Date);

        // Corrupt one committed part and leave an orphaned .tmp (interrupted write).
        File.WriteAllBytes(parts[1], new byte[] { 0x50, 0x41, 0x52, 0x31, 0x00 });
        File.WriteAllText(Path.Combine(sessionDir, "events-9999.parquet.tmp"), "partial");

        var detect = await new DataRepairService().ScanAsync(sessionDir, quarantine: false);
        Assert.False(detect.IsHealthy);
        Assert.Equal(1, detect.CorruptParts);
        Assert.Equal(1, detect.OrphanedTempFiles);

        var repaired = await new DataRepairService().ScanAsync(sessionDir, quarantine: true);
        Assert.Equal(2, repaired.Quarantined); // corrupt part + temp file
        Assert.True(Directory.Exists(Path.Combine(sessionDir, "_quarantine")));

        // After repair, a fresh scan is healthy and the remaining parts still replay.
        var after = await new DataRepairService().ScanAsync(sessionDir, quarantine: false);
        Assert.True(after.IsHealthy);

        var source = new ParquetReplaySource(_layout, RootSymbol.NQ, Nq.Symbol, Date);
        int replayed = 0;
        await foreach (var _ in source.ReadAllAsync(default)) replayed++;
        Assert.True(replayed > 0);
        Assert.Equal(0, source.CorruptParts);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }
}
