namespace FlowTerminal.Charting.Studies;

public enum StudyCategory
{
    VolumeAndProfile,
    OrderFlowAndTape,
    TechnicalAndPriceAction,
}

public enum StudyStatus
{
    /// <summary>Computed and visible in the shell now.</summary>
    Active,

    /// <summary>Engine implemented and unit-tested; chart overlay wiring is pending.</summary>
    EngineReady,

    /// <summary>Not yet implemented.</summary>
    Planned,
}

/// <summary>
/// One study in the catalog. <see cref="DetectorKey"/> links a study to a runtime
/// detector toggle when applicable.
/// </summary>
public sealed record StudyDefinition(
    string Name,
    string ShortCode,
    StudyCategory Category,
    StudyStatus Status,
    string Description,
    string? DetectorKey = null);

/// <summary>
/// The catalog of available studies (à la a "Studies" tab). Each entry honestly
/// reflects whether it is Active, EngineReady (computed + tested, overlay pending),
/// or Planned — nothing here is a fake placeholder.
/// </summary>
public static class StudyCatalog
{
    public static IReadOnlyList<StudyDefinition> All { get; } = new[]
    {
        // ── Volume & Profile ─────────────────────────────────────────────
        new StudyDefinition("Volume Profile (VBP)", "VBP", StudyCategory.VolumeAndProfile, StudyStatus.Active,
            "Total volume traded at each price over a session; locates high/low volume nodes."),
        new StudyDefinition("Bid/Ask Cluster Profile", "BAC", StudyCategory.VolumeAndProfile, StudyStatus.EngineReady,
            "Splits horizontal volume into buying (ask) and selling (bid) segments at each price."),
        new StudyDefinition("Delta Profile", "DP", StudyCategory.VolumeAndProfile, StudyStatus.EngineReady,
            "Net aggressive buyers minus sellers at each price increment."),
        new StudyDefinition("TPO / Market Profile", "TPO", StudyCategory.VolumeAndProfile, StudyStatus.EngineReady,
            "Time spent at price as lettered brackets; RTH/ETH split by session window."),
        new StudyDefinition("Volume Profile on Swing", "VPS", StudyCategory.VolumeAndProfile, StudyStatus.Planned,
            "Profiles anchored across micro-structural market swings (swing detector pending)."),
        new StudyDefinition("VWAP (daily/weekly/monthly/anchored)", "VWAP", StudyCategory.VolumeAndProfile, StudyStatus.EngineReady,
            "Volume-weighted average price with std-dev bands; periodic and manually anchored."),

        // ── Order Flow & Tape ────────────────────────────────────────────
        new StudyDefinition("Footprint / Cluster Chart", "FP", StudyCategory.OrderFlowAndTape, StudyStatus.EngineReady,
            "Per-candle bid/ask execution volumes at each price."),
        new StudyDefinition("Volume Imbalance Tracker", "IMB", StudyCategory.OrderFlowAndTape, StudyStatus.EngineReady,
            "Stacked diagonal aggressive buy/sell imbalances across the spread."),
        new StudyDefinition("Cumulative Volume Delta (CVD)", "CVD", StudyCategory.OrderFlowAndTape, StudyStatus.Active,
            "Net continuous accumulation/distribution of aggressive orders; isolates divergence."),
        new StudyDefinition("Large / Block Trade Detector", "LT", StudyCategory.OrderFlowAndTape, StudyStatus.Active,
            "Flags outsized block/institutional market orders in real time.", DetectorKey: "Large Trade"),
        new StudyDefinition("Iceberg & Absorption Detector", "ICE", StudyCategory.OrderFlowAndTape, StudyStatus.Active,
            "Hidden refilling orders (estimated) and heavy passive absorption.", DetectorKey: "Iceberg"),
        new StudyDefinition("Speed of Tape (SOT)", "SOT", StudyCategory.OrderFlowAndTape, StudyStatus.EngineReady,
            "Transactions/contracts per second; quantifies HFT/algo aggression."),
        new StudyDefinition("Stop Run / Liquidity Hunt", "SR", StudyCategory.OrderFlowAndTape, StudyStatus.Active,
            "Sweeps through swing levels with volume bursts that run stops.", DetectorKey: "Stop-Run"),

        // ── Technical Analysis & Price Action ────────────────────────────
        new StudyDefinition("Fair Value Gap (FVG)", "FVG", StudyCategory.TechnicalAndPriceAction, StudyStatus.EngineReady,
            "3-bar structural inefficiencies / liquidity voids."),
        new StudyDefinition("Opening Range Breakout (ORB)", "ORB", StudyCategory.TechnicalAndPriceAction, StudyStatus.EngineReady,
            "Maps the opening-range high/low and flags the first break."),
        new StudyDefinition("Average Daily Range (ADR)", "ADR", StudyCategory.TechnicalAndPriceAction, StudyStatus.EngineReady,
            "Trailing daily-range average projected as session extension targets."),
        new StudyDefinition("Multi-Timeframe Candle Overlay", "MTF", StudyCategory.TechnicalAndPriceAction, StudyStatus.Planned,
            "Higher-period candles drawn onto a lower-period chart (overlay pending)."),
        new StudyDefinition("Chaikin Accumulation/Distribution", "AD", StudyCategory.TechnicalAndPriceAction, StudyStatus.EngineReady,
            "Volume-proxy A/D line assessing institutional pressure."),
        new StudyDefinition("Gap Detector", "GAP", StudyCategory.TechnicalAndPriceAction, StudyStatus.EngineReady,
            "Highlights weekend/overnight opening price gaps."),
    };

    public static IEnumerable<StudyDefinition> ByCategory(StudyCategory category) =>
        All.Where(s => s.Category == category);

    public static string CategoryTitle(StudyCategory c) => c switch
    {
        StudyCategory.VolumeAndProfile => "Volume & Profile",
        StudyCategory.OrderFlowAndTape => "Order Flow & Tape",
        StudyCategory.TechnicalAndPriceAction => "Technical & Price Action",
        _ => c.ToString(),
    };

    public static string StatusLabel(StudyStatus s) => s switch
    {
        StudyStatus.Active => "ACTIVE",
        StudyStatus.EngineReady => "READY",
        StudyStatus.Planned => "PLANNED",
        _ => s.ToString(),
    };
}
