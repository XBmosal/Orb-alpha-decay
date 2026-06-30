using FlowTerminal.Analytics.Bars;

namespace FlowTerminal.Analytics.Indicators;

/// <summary>
/// The price input an indicator computes on. Values are in integer ticks expressed
/// as <see cref="double"/> (the natural unit for moving averages and oscillators);
/// callers convert to/from decimal prices at the display boundary only.
/// </summary>
public enum PriceSource
{
    Close,
    Open,
    High,
    Low,
    Hl2,          // (H+L)/2 — median price
    Hlc3,         // (H+L+C)/3 — typical price
    Ohlc4,        // (O+H+L+C)/4 — average price
    Typical,      // alias of Hlc3, kept for readability at call sites
    WeightedClose // (H+L+2C)/4
}

public static class PriceSourceExtensions
{
    /// <summary>Selects the configured price (in ticks, as a double) from a bar.</summary>
    public static double Select(this PriceSource source, in Bar bar)
    {
        double o = bar.OpenTicks, h = bar.HighTicks, l = bar.LowTicks, c = bar.CloseTicks;
        return source switch
        {
            PriceSource.Open => o,
            PriceSource.High => h,
            PriceSource.Low => l,
            PriceSource.Close => c,
            PriceSource.Hl2 => (h + l) / 2.0,
            PriceSource.Hlc3 or PriceSource.Typical => (h + l + c) / 3.0,
            PriceSource.Ohlc4 => (o + h + l + c) / 4.0,
            PriceSource.WeightedClose => (h + l + 2.0 * c) / 4.0,
            _ => c,
        };
    }
}

/// <summary>Top-level grouping for the indicator catalogue (mirrors the spec's categories).</summary>
public enum IndicatorCategory
{
    Trend,
    Momentum,
    Volatility,
    Volume,
    OrderFlow,
    Profile,
    Vwap,
    MarketStructure,
    Session,
    Overlay,
    Utility,
    Experimental,
}

/// <summary>
/// The market-data capabilities an indicator requires. Declared per indicator so the
/// UI can honestly show Available / Unavailable / Estimated states and never silently
/// compute (for example) an MBO study from MBP depth.
/// </summary>
[Flags]
public enum IndicatorData
{
    None = 0,
    Ohlc = 1 << 0,
    Volume = 1 << 1,
    TradeCount = 1 << 2,
    AggressorSide = 1 << 3,
    BidAskVolume = 1 << 4,
    PriceLevelVolume = 1 << 5,
    MbpDepth = 1 << 6,
    MboDepth = 1 << 7,
    ExchangeTimestamps = 1 << 8,
    SequenceNumbers = 1 << 9,
    Session = 1 << 10,
}

/// <summary>
/// Static, serialisable metadata describing an indicator: identity, category, data
/// requirements, placement, warm-up and repaint behaviour. Calculation, state,
/// settings and rendering are intentionally kept out of this record (see the spec's
/// separation requirement) — this is metadata only.
/// </summary>
public sealed record IndicatorDescriptor(
    string Id,
    string Name,
    IndicatorCategory Category,
    string Description,
    IndicatorData RequiredData,
    bool Overlay,
    int WarmupBars,
    bool Repaints = false,
    bool Estimated = false,
    bool Experimental = false,
    string Version = "1.0");

/// <summary>Common contract for every indicator: it exposes metadata and can be reset.</summary>
public interface IIndicator
{
    IndicatorDescriptor Descriptor { get; }

    /// <summary>True once enough data has been seen to emit a defined value.</summary>
    bool IsReady { get; }

    /// <summary>Clears all incremental state (session/contract/timeframe change, replay seek).</summary>
    void Reset();
}

/// <summary>An indicator driven by a stream of completed bars.</summary>
public interface IBarIndicator : IIndicator
{
    /// <summary>Feeds the next confirmed bar. Must never look at future bars.</summary>
    void OnBar(in Bar bar);
}

/// <summary>An indicator driven by a stream of scalar values (e.g. a chosen price source).</summary>
public interface IScalarIndicator : IIndicator
{
    /// <summary>Feeds the next scalar input.</summary>
    void OnValue(double value);

    /// <summary>Latest output, or <see cref="double.NaN"/> until warmed up.</summary>
    double Value { get; }
}
