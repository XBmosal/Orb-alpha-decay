namespace FlowTerminal.Analytics.Footprints;

/// <summary>How each footprint cell is displayed.</summary>
public enum FootprintMode
{
    BidAsk,        // "bid × ask"
    Delta,         // ask − bid per level
    TotalVolume,   // bid + ask (+ unknown)
    BidOnly,
    AskOnly,
    DeltaPercent,  // delta / total × 100
    TradeCount,
    VolumeProfile, // horizontal volume bars (silhouette)
}

/// <summary>Which metric the bar Point of Control maximises.</summary>
public enum PocSource
{
    TotalVolume,
    BidVolume,
    AskVolume,
    AbsoluteDelta,
    TradeCount,
}

/// <summary>How a diagonal imbalance with a zero comparison volume is treated.</summary>
public enum ZeroDenominatorPolicy
{
    /// <summary>A zero denominator never qualifies.</summary>
    Ignore,

    /// <summary>Qualifies only if the dominant side meets a minimum numerator (MinImbalanceVolume).</summary>
    RequireMinNumerator,

    /// <summary>Any non-zero dominant side over zero qualifies as an extreme imbalance.</summary>
    ExtremeImbalance,

    /// <summary>Substitute a denominator floor and apply the normal ratio test.</summary>
    DenominatorFloor,
}

/// <summary>How the large-trade threshold is derived.</summary>
public enum LargeTradeThresholdMode
{
    Fixed,
    SessionPercentile,
}

/// <summary>Confirmed/developing lifecycle for extreme-row features.</summary>
public enum AuctionState
{
    None,
    Developing,
    Confirmed,
}

/// <summary>
/// All footprint configuration in one validated, serialisable record. Defaults suit
/// the synthetic NQ feed; <see cref="Nq"/>/<see cref="Es"/> give per-instrument
/// presets. <see cref="Validate"/> clamps every value into a safe range so the
/// renderer and aggregator can never divide by zero or read a negative threshold.
/// </summary>
public sealed record FootprintSettings
{
    public FootprintMode Mode { get; init; } = FootprintMode.BidAsk;

    // Diagonal imbalance.
    public bool EnableDiagonalImbalance { get; init; } = true;
    public bool EnableHorizontalImbalance { get; init; }
    public double BuyImbalanceRatio { get; init; } = 3.0;   // 300%
    public double SellImbalanceRatio { get; init; } = 3.0;
    public long MinImbalanceVolume { get; init; } = 1;       // minimum dominant-side volume
    public ZeroDenominatorPolicy ZeroDenominator { get; init; } = ZeroDenominatorPolicy.RequireMinNumerator;
    public long DenominatorFloor { get; init; } = 1;

    // Stacked imbalance.
    public int StackedCount { get; init; } = 3;
    public bool AllowOneGap { get; init; }
    public long MinStackVolume { get; init; }

    // POC.
    public bool ShowPoc { get; init; } = true;
    public PocSource PocSource { get; init; } = PocSource.TotalVolume;

    // Zero prints.
    public bool ShowZeroPrints { get; init; } = true;
    public long ZeroPrintMinOpposite { get; init; } = 1;
    public bool ZeroPrintExcludeExtremes { get; init; } = true;

    // Unfinished auctions.
    public bool ShowUnfinishedAuctions { get; init; } = true;
    public long UnfinishedMinVolume { get; init; } = 1;

    // Large trades.
    public bool ShowLargeTrades { get; init; } = true;
    public LargeTradeThresholdMode LargeTradeMode { get; init; } = LargeTradeThresholdMode.Fixed;
    public long LargeTradeThreshold { get; init; } = 50;
    public double LargeTradePercentile { get; init; } = 0.99;

    // Delta / unknown handling.
    public bool IncludeUnknownInTotal { get; init; } = true;

    /// <summary>Returns a copy with every field clamped into a valid range.</summary>
    public FootprintSettings Validate() => this with
    {
        BuyImbalanceRatio = Math.Clamp(BuyImbalanceRatio, 1.01, 100.0),
        SellImbalanceRatio = Math.Clamp(SellImbalanceRatio, 1.01, 100.0),
        MinImbalanceVolume = Math.Max(0, MinImbalanceVolume),
        DenominatorFloor = Math.Max(1, DenominatorFloor),
        StackedCount = Math.Clamp(StackedCount, 2, 50),
        MinStackVolume = Math.Max(0, MinStackVolume),
        ZeroPrintMinOpposite = Math.Max(0, ZeroPrintMinOpposite),
        UnfinishedMinVolume = Math.Max(0, UnfinishedMinVolume),
        LargeTradeThreshold = Math.Max(1, LargeTradeThreshold),
        LargeTradePercentile = Math.Clamp(LargeTradePercentile, 0.5, 0.9999),
    };

    public static FootprintSettings Default { get; } = new();

    /// <summary>NQ preset (thinner book, larger travel; large-trade threshold tuned for mock NQ).</summary>
    public static FootprintSettings Nq { get; } = new()
    {
        LargeTradeThreshold = 45,
        MinImbalanceVolume = 2,
    };

    /// <summary>ES preset (heavier per-level size; higher large-trade threshold).</summary>
    public static FootprintSettings Es { get; } = new()
    {
        LargeTradeThreshold = 75,
        MinImbalanceVolume = 4,
    };
}
