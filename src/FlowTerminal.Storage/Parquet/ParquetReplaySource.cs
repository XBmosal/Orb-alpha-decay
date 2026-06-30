using System.Runtime.CompilerServices;
using FlowTerminal.Domain.Events;
using FlowTerminal.Domain.Instruments;
using FlowTerminal.MarketData.Abstractions;
using Parquet.Serialization;

namespace FlowTerminal.Storage.Parquet;

/// <summary>
/// Replays a recorded session by reading its ordered Parquet part files. Each part
/// is a self-contained, valid Parquet file; a truncated/corrupt part (e.g. from a
/// crash mid-write) is detected and skipped rather than aborting the whole replay,
/// and counted in <see cref="CorruptParts"/>. Replay yields events in recorded order
/// and never uses future information.
/// </summary>
public sealed class ParquetReplaySource : IReplaySource
{
    private readonly RecordingLayout _layout;
    private readonly RootSymbol _root;
    private readonly string _contractSymbol;
    private readonly DateOnly _tradingDate;

    public ParquetReplaySource(RecordingLayout layout, RootSymbol root, string contractSymbol, DateOnly tradingDate)
    {
        _layout = layout;
        _root = root;
        _contractSymbol = contractSymbol;
        _tradingDate = tradingDate;
    }

    public int CorruptParts { get; private set; }

    public IAsyncEnumerable<MarketEvent> ReadAsync(ReplayRequest request, CancellationToken cancellationToken) =>
        ReadInternalAsync(request.FromUtc, request.ToUtc, cancellationToken);

    /// <summary>Reads the entire recorded session in order.</summary>
    public IAsyncEnumerable<MarketEvent> ReadAllAsync(CancellationToken cancellationToken) =>
        ReadInternalAsync(DateTime.MinValue, DateTime.MaxValue, cancellationToken);

    private async IAsyncEnumerable<MarketEvent> ReadInternalAsync(
        DateTime fromUtc, DateTime toUtc, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        CorruptParts = 0;
        foreach (var part in _layout.EnumerateParts(_root, _contractSymbol, _tradingDate))
        {
            IList<MarketEventRecord>? records = null;
            try
            {
                await using var stream = new FileStream(part, FileMode.Open, FileAccess.Read, FileShare.Read);
                records = await ParquetSerializer.DeserializeAsync<MarketEventRecord>(stream, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Truncated/corrupt part (e.g. interrupted write): skip and continue.
                CorruptParts++;
            }

            if (records is null)
            {
                continue;
            }

            foreach (var record in records)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    yield break;
                }

                if (record.ExchangeTimestampUtc < fromUtc || record.ExchangeTimestampUtc > toUtc)
                {
                    continue;
                }

                yield return record.ToEvent();
            }
        }
    }
}
