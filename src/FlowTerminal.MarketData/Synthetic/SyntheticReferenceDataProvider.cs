using FlowTerminal.Domain.Instruments;
using FlowTerminal.Domain.Time;
using FlowTerminal.MarketData.Abstractions;

namespace FlowTerminal.MarketData.Synthetic;

/// <summary>
/// Contract discovery for mock mode. Enumerates the standard quarterly NQ/ES
/// cycle around "today" and suggests the front month using the shared
/// <see cref="ContractCalendar"/>. No symbols are hard-coded.
/// </summary>
public sealed class SyntheticReferenceDataProvider : IReferenceDataProvider
{
    private readonly IClock _clock;
    private readonly ContractCalendar _calendar;

    public SyntheticReferenceDataProvider(IClock? clock = null, ContractCalendar? calendar = null)
    {
        _clock = clock ?? SystemClock.Instance;
        _calendar = calendar ?? new ContractCalendar();
    }

    public Task<IReadOnlyList<Contract>> DiscoverContractsAsync(RootSymbol root, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(_clock.UtcNow);
        var contracts = _calendar.Enumerate(root, today.Year, today.Year + 1);
        return Task.FromResult(contracts);
    }

    public Task<Contract> GetSuggestedActiveContractAsync(RootSymbol root, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(_clock.UtcNow);
        return Task.FromResult(_calendar.SuggestActive(root, today));
    }
}
