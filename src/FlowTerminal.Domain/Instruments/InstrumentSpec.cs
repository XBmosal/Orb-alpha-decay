namespace FlowTerminal.Domain.Instruments;

/// <summary>
/// Immutable contract specification for a root symbol. All values are exact
/// (decimal, never binary float) so that tick conversion and currency math are
/// reproducible. Prices are converted to integer ticks for all internal storage
/// and processing; decimals are used only at the display / export boundary.
/// </summary>
public sealed class InstrumentSpec
{
    public InstrumentSpec(
        RootSymbol root,
        string rootSymbol,
        string exchange,
        decimal tickSize,
        decimal pointValue,
        string currency,
        string description)
    {
        if (tickSize <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(tickSize), tickSize, "Tick size must be positive.");
        }

        if (pointValue <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(pointValue), pointValue, "Point value must be positive.");
        }

        Root = root;
        RootSymbol = rootSymbol;
        Exchange = exchange;
        TickSize = tickSize;
        PointValue = pointValue;
        Currency = currency;
        Description = description;
    }

    public RootSymbol Root { get; }

    /// <summary>Exchange root symbol, e.g. "NQ" or "ES".</summary>
    public string RootSymbol { get; }

    /// <summary>Listing exchange, e.g. "CME Globex".</summary>
    public string Exchange { get; }

    /// <summary>Minimum price increment in index points (e.g. 0.25).</summary>
    public decimal TickSize { get; }

    /// <summary>Currency value of one full index point (NQ = $20, ES = $50).</summary>
    public decimal PointValue { get; }

    /// <summary>Currency value of a single tick = <see cref="TickSize"/> * <see cref="PointValue"/>.</summary>
    public decimal TickValue => TickSize * PointValue;

    /// <summary>ISO currency code, e.g. "USD".</summary>
    public string Currency { get; }

    public string Description { get; }
}
