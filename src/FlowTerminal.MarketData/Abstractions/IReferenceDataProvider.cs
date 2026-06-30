using FlowTerminal.Domain.Instruments;

namespace FlowTerminal.MarketData.Abstractions;

/// <summary>
/// Discovers tradable contracts for the supported roots. Contract symbols are
/// never hard-coded; they come from here. The "suggested active" contract is
/// advisory — the user always confirms the selection.
/// </summary>
public interface IReferenceDataProvider
{
    Task<IReadOnlyList<Contract>> DiscoverContractsAsync(RootSymbol root, CancellationToken cancellationToken);

    Task<Contract> GetSuggestedActiveContractAsync(RootSymbol root, CancellationToken cancellationToken);
}
