using FlowTerminal.Domain.Capabilities;
using FlowTerminal.Domain.Events;
using FlowTerminal.Domain.Instruments;
using FlowTerminal.MarketData.Abstractions;

namespace FlowTerminal.Rithmic;

/// <summary>
/// Authorized Rithmic historical-data adapter boundary. Returns no data and never
/// fabricates history until the authorized SDK is installed and wired in. In
/// particular, historical depth is only ever returned when the entitled feed
/// actually supplies it — it is never manufactured.
/// </summary>
public sealed class RithmicHistoricalDataProvider : IHistoricalDataProvider
{
    public ProviderCapabilities Capabilities { get; } = new("Rithmic Historical (not connected)", DataCapabilities.None);

    public IAsyncEnumerable<MarketEvent> GetHistoricalTradesAsync(
        Contract contract, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken)
        => throw new RithmicSdkUnavailableException();
}

/// <summary>
/// Authorized Rithmic reference-data adapter boundary. Contract discovery comes
/// from the SDK's reference-data services once installed; nothing is hard-coded.
/// </summary>
public sealed class RithmicReferenceDataProvider : IReferenceDataProvider
{
    public Task<IReadOnlyList<Contract>> DiscoverContractsAsync(RootSymbol root, CancellationToken cancellationToken)
        => throw new RithmicSdkUnavailableException();

    public Task<Contract> GetSuggestedActiveContractAsync(RootSymbol root, CancellationToken cancellationToken)
        => throw new RithmicSdkUnavailableException();
}
