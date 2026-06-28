using System.Runtime.CompilerServices;
using FlowTerminal.Domain.Capabilities;
using FlowTerminal.Domain.Events;
using FlowTerminal.Domain.Instruments;
using FlowTerminal.MarketData.Abstractions;

namespace FlowTerminal.MarketData.Synthetic;

/// <summary>
/// Deterministic synthetic history. Produces reproducible trade events across a
/// requested UTC window. It honestly advertises only trades + bars (no historical
/// depth), matching what synthetic generation can support.
/// </summary>
public sealed class SyntheticHistoricalDataProvider : IHistoricalDataProvider
{
    private readonly int _instrumentId;
    private readonly SyntheticOptions _options;

    public SyntheticHistoricalDataProvider(int instrumentId, SyntheticOptions? options = null)
    {
        _instrumentId = instrumentId;
        _options = options ?? new SyntheticOptions();
    }

    public ProviderCapabilities Capabilities { get; } = new(
        "Synthetic Historical",
        DataCapabilities.HistoricalTrades | DataCapabilities.HistoricalBars |
        DataCapabilities.ExchangeTimestamps | DataCapabilities.SequenceNumbers |
        DataCapabilities.AggressorSideFlags);

    public async IAsyncEnumerable<MarketEvent> GetHistoricalTradesAsync(
        Contract contract, DateTime fromUtc, DateTime toUtc,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (toUtc <= fromUtc)
        {
            yield break;
        }

        var generator = new SyntheticSessionGenerator(_instrumentId, contract, fromUtc, _options);

        while (!cancellationToken.IsCancellationRequested)
        {
            var ev = generator.Next();
            if (ev.ExchangeTimestampUtc >= toUtc)
            {
                yield break;
            }

            if (ev.Type == MarketEventType.Trade)
            {
                yield return ev;
            }

            await Task.Yield();
        }
    }
}
