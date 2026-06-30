using FlowTerminal.Domain.Capabilities;
using FlowTerminal.Domain.Events;
using FlowTerminal.Domain.Instruments;

namespace FlowTerminal.MarketData.Abstractions;

/// <summary>
/// Historical data access. Implementations must only return data they actually
/// possess: historical depth, for example, is unavailable unless it was recorded
/// or the feed is entitled. Never manufacture historical depth.
/// </summary>
public interface IHistoricalDataProvider
{
    ProviderCapabilities Capabilities { get; }

    /// <summary>Historical trades for a contract over a UTC range, in ascending time order.</summary>
    IAsyncEnumerable<MarketEvent> GetHistoricalTradesAsync(
        Contract contract, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken);
}
