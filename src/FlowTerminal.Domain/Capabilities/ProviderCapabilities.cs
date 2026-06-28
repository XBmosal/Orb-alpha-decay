namespace FlowTerminal.Domain.Capabilities;

/// <summary>
/// The set of data capabilities a market-data provider may expose. Features that
/// depend on an absent capability must be disabled gracefully (e.g. queue analysis
/// requires <see cref="MarketByOrderDepth"/>) and values derived without exchange
/// support must be labelled "Estimated".
/// </summary>
[Flags]
public enum DataCapabilities : uint
{
    None = 0,
    Trades = 1 << 0,
    TopOfBook = 1 << 1,
    MarketByPriceDepth = 1 << 2,
    MarketByOrderDepth = 1 << 3,
    HistoricalTrades = 1 << 4,
    HistoricalBars = 1 << 5,
    HistoricalDepth = 1 << 6,
    ExchangeTimestamps = 1 << 7,
    SequenceNumbers = 1 << 8,
    ImpliedLiquidityFlags = 1 << 9,
    AggressorSideFlags = 1 << 10,
}

/// <summary>
/// A provider's advertised capabilities, surfaced to the diagnostics panel so the
/// user always sees honestly what the active feed can and cannot do.
/// </summary>
public sealed class ProviderCapabilities
{
    public ProviderCapabilities(string providerName, DataCapabilities capabilities)
    {
        ProviderName = providerName;
        Capabilities = capabilities;
    }

    public string ProviderName { get; }

    public DataCapabilities Capabilities { get; }

    public bool Has(DataCapabilities capability) => (Capabilities & capability) == capability;

    /// <summary>True when aggressor side must be inferred (and therefore labelled Estimated).</summary>
    public bool AggressorIsEstimated => !Has(DataCapabilities.AggressorSideFlags);

    /// <summary>True when queue-position / MBO analysis can be offered.</summary>
    public bool SupportsQueueAnalysis => Has(DataCapabilities.MarketByOrderDepth);

    public IEnumerable<DataCapabilities> Enumerate()
    {
        foreach (DataCapabilities cap in Enum.GetValues<DataCapabilities>())
        {
            if (cap != DataCapabilities.None && Has(cap))
            {
                yield return cap;
            }
        }
    }
}
