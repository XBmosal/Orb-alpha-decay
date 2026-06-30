namespace FlowTerminal.Domain.Instruments;

/// <summary>
/// Converts between exchange prices (decimal index points) and the integer-tick
/// representation used everywhere internally. Binary floating point is never used
/// for prices; all conversion goes through <see cref="decimal"/>.
///
/// Contract:
///   priceTicks = round(exchangePrice / tickSize)         (banker's rounding)
///   exchangePrice = priceTicks * tickSize
/// </summary>
public static class PriceConverter
{
    /// <summary>
    /// Converts an exchange price to integer ticks for the given instrument.
    /// Uses <see cref="MidpointRounding.ToEven"/> so half-tick artifacts from a
    /// feed do not bias systematically up or down.
    /// </summary>
    public static long ToTicks(InstrumentSpec spec, decimal exchangePrice)
    {
        ArgumentNullException.ThrowIfNull(spec);
        decimal ticks = decimal.Round(exchangePrice / spec.TickSize, 0, MidpointRounding.ToEven);
        return (long)ticks;
    }

    /// <summary>Converts integer ticks back to an exchange price (decimal index points).</summary>
    public static decimal ToPrice(InstrumentSpec spec, long priceTicks)
    {
        ArgumentNullException.ThrowIfNull(spec);
        return priceTicks * spec.TickSize;
    }

    /// <summary>Currency value of a tick-denominated quantity at the given size (ticks * tickValue * size).</summary>
    public static decimal TickProfitValue(InstrumentSpec spec, long priceTicks, long quantity)
    {
        ArgumentNullException.ThrowIfNull(spec);
        return priceTicks * spec.TickValue * quantity;
    }

    /// <summary>
    /// Returns true if the supplied exchange price lands exactly on a tick boundary.
    /// Off-tick prices indicate a feed or normalization problem and should be flagged.
    /// </summary>
    public static bool IsOnTick(InstrumentSpec spec, decimal exchangePrice)
    {
        ArgumentNullException.ThrowIfNull(spec);
        return decimal.Remainder(exchangePrice, spec.TickSize) == 0m;
    }
}
