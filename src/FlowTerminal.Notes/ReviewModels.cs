using FlowTerminal.Domain.Instruments;

namespace FlowTerminal.Notes;

/// <summary>Filter for querying notes/bookmarks/annotations. All criteria are optional (AND-combined).</summary>
public sealed record ReviewQuery
{
    public RootSymbol? Root { get; init; }
    public string? ContractSymbol { get; init; }
    public string? Tag { get; init; }
    public DateOnly? FromDate { get; init; }
    public DateOnly? ToDate { get; init; }
}

/// <summary>
/// A bookmarked moment that links to a replay timestamp. Selecting a bookmark in the
/// UI jumps replay to <see cref="ReplayTimestampUtc"/>.
/// </summary>
public sealed record Bookmark(
    Guid Id,
    DateTime CreatedUtc,
    DateTime ReplayTimestampUtc,
    RootSymbol Root,
    string ContractSymbol,
    string Label);

public enum TradeDirection
{
    Long,
    Short,
}

public enum TradeOutcome
{
    Open,
    Win,
    Loss,
    Scratch,
}

/// <summary>
/// A MANUAL annotation of a hypothetical or observed trade. These are entered by the
/// user for review only and must NEVER be presented as broker-confirmed trades —
/// <see cref="IsManualUnverified"/> is always true. Prices are integer ticks.
/// </summary>
public sealed record ManualAnnotation(
    Guid Id,
    DateTime CreatedUtc,
    RootSymbol Root,
    string ContractSymbol,
    TradeDirection Direction,
    long EntryPriceTicks,
    long? ExitPriceTicks,
    long? StopPriceTicks,
    long? TargetPriceTicks,
    TradeOutcome Outcome,
    string Notes)
{
    /// <summary>Always true: this is a manually entered annotation, not a confirmed fill.</summary>
    public bool IsManualUnverified => true;

    /// <summary>
    /// Risk/reward in ticks from the entry/stop/target, when both are present. Purely a
    /// drawing-board figure derived from the user's own inputs.
    /// </summary>
    public double? RiskRewardRatio
    {
        get
        {
            if (StopPriceTicks is not { } stop || TargetPriceTicks is not { } target) return null;
            long risk = Math.Abs(EntryPriceTicks - stop);
            long reward = Math.Abs(target - EntryPriceTicks);
            return risk == 0 ? null : reward / (double)risk;
        }
    }
}
