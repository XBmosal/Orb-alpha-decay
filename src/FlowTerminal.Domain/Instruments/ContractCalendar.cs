namespace FlowTerminal.Domain.Instruments;

/// <summary>
/// Enumerates quarterly NQ/ES contracts and produces a <em>suggested</em> active
/// front-month contract. The suggestion is advisory only — the user always remains
/// in control of the selected contract and the app never silently changes it.
///
/// The default heuristic rolls a configurable number of days before expiration
/// (volume-based rolling is applied by callers when entitled volume data exists).
/// </summary>
public sealed class ContractCalendar
{
    private static readonly QuarterlyMonth[] CycleOrder =
    {
        QuarterlyMonth.March,
        QuarterlyMonth.June,
        QuarterlyMonth.September,
        QuarterlyMonth.December,
    };

    private readonly int _rollDaysBeforeExpiration;

    public ContractCalendar(int rollDaysBeforeExpiration = 8)
    {
        if (rollDaysBeforeExpiration < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rollDaysBeforeExpiration));
        }

        _rollDaysBeforeExpiration = rollDaysBeforeExpiration;
    }

    /// <summary>Quarterly contracts for a root in ascending expiration order over a span of years.</summary>
    public IReadOnlyList<Contract> Enumerate(RootSymbol root, int fromYear, int toYear)
    {
        if (toYear < fromYear)
        {
            throw new ArgumentOutOfRangeException(nameof(toYear));
        }

        var list = new List<Contract>((toYear - fromYear + 1) * CycleOrder.Length);
        for (int year = fromYear; year <= toYear; year++)
        {
            foreach (var month in CycleOrder)
            {
                list.Add(new Contract(root, month, year));
            }
        }

        list.Sort(static (a, b) => a.ExpirationDateUtc.CompareTo(b.ExpirationDateUtc));
        return list;
    }

    /// <summary>
    /// Suggested active contract for the given date: the earliest-expiring contract
    /// whose expiration is at least <c>rollDaysBeforeExpiration</c> in the future.
    /// </summary>
    public Contract SuggestActive(RootSymbol root, DateOnly today)
    {
        foreach (var contract in Enumerate(root, today.Year - 1, today.Year + 2))
        {
            if (contract.DaysUntilExpiration(today) >= _rollDaysBeforeExpiration)
            {
                return contract;
            }
        }

        // Fallback: the next December far enough out (should be unreachable in practice).
        return new Contract(root, QuarterlyMonth.December, today.Year + 1);
    }

    /// <summary>
    /// True when the suggested contract is within the rollover window and the user
    /// should be warned that a roll is approaching. The warning never rolls automatically.
    /// </summary>
    public bool IsInRolloverWindow(Contract contract, DateOnly today) =>
        contract.DaysUntilExpiration(today) <= _rollDaysBeforeExpiration;
}
