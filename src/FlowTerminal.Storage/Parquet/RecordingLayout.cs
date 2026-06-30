using FlowTerminal.Domain.Instruments;

namespace FlowTerminal.Storage.Parquet;

/// <summary>
/// Resolves the on-disk recording layout:
///   data/venue=CME/root=&lt;NQ|ES&gt;/contract=&lt;symbol&gt;/date=&lt;yyyy-MM-dd&gt;/events-NNNN.parquet
///
/// The canonical event log is stored as ordered <em>part files</em>. Appending a new
/// part (rather than rewriting one growing file) keeps writes cheap and crash-safe:
/// a partially written part is simply skipped/repaired, and earlier parts remain
/// valid. Derived projections (trades/depth/bars) are produced on demand via DuckDB.
/// </summary>
public sealed class RecordingLayout
{
    public RecordingLayout(string dataRoot) => DataRoot = dataRoot;

    public string DataRoot { get; }

    public string SessionDirectory(RootSymbol root, string contractSymbol, DateOnly tradingDate) =>
        Path.Combine(
            DataRoot,
            "venue=CME",
            $"root={root}",
            $"contract={contractSymbol}",
            $"date={tradingDate:yyyy-MM-dd}");

    public string PartFile(RootSymbol root, string contractSymbol, DateOnly tradingDate, int partIndex) =>
        Path.Combine(SessionDirectory(root, contractSymbol, tradingDate), $"events-{partIndex:D4}.parquet");

    /// <summary>Existing event part files for a session, in ascending part order.</summary>
    public IReadOnlyList<string> EnumerateParts(RootSymbol root, string contractSymbol, DateOnly tradingDate)
    {
        var dir = SessionDirectory(root, contractSymbol, tradingDate);
        if (!Directory.Exists(dir))
        {
            return Array.Empty<string>();
        }

        var files = Directory.GetFiles(dir, "events-*.parquet");
        Array.Sort(files, StringComparer.Ordinal);
        return files;
    }
}
