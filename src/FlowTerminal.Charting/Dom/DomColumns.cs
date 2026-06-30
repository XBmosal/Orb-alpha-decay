namespace FlowTerminal.Charting.Dom;

/// <summary>The analytical column types a DOM ladder can show (all read-only).</summary>
public enum DomColumnType
{
    Price,
    BidSize,
    AskSize,
    BidCumulative,
    AskCumulative,
    BidExecuted,    // sells (traded at bid)
    AskExecuted,    // buys (traded at ask)
    Delta,
    BidPullStack,
    AskPullStack,
    BidRefill,
    AskRefill,
    BidRelative,
    AskRelative,
    MaxTrade,
    TradeCount,
    BidOrders,       // MBO
    AskOrders,       // MBO
    BidLargestOrder, // MBO
    AskLargestOrder, // MBO
}

public enum DomColumnSide { Bid, Center, Ask, Neutral }

public enum DomColumnAlign { Left, Center, Right }

/// <summary>What data a column needs — drives the capability badge (MBP/MBO/Trades).</summary>
[Flags]
public enum DomDataRequirement { Mbp = 1, Trades = 2, Mbo = 4 }

/// <summary>Static, serialisable metadata for one DOM column. No calculation or rendering here.</summary>
public sealed record DomColumnDescriptor(
    DomColumnType Type,
    string Id,
    string ShortHeader,
    string FullName,
    DomColumnSide Side,
    DomDataRequirement Requirement,
    double DefaultWidth,
    DomColumnAlign Align,
    bool Estimated = false);

/// <summary>The catalogue of available DOM columns + their capability requirements.</summary>
public static class DomColumnRegistry
{
    public static IReadOnlyList<DomColumnDescriptor> All { get; } = new[]
    {
        new DomColumnDescriptor(DomColumnType.Price, "price", "Price", "Price", DomColumnSide.Center, DomDataRequirement.Mbp, 74, DomColumnAlign.Center),
        new DomColumnDescriptor(DomColumnType.BidSize, "bid", "Bid", "Bid size", DomColumnSide.Bid, DomDataRequirement.Mbp, 64, DomColumnAlign.Right),
        new DomColumnDescriptor(DomColumnType.AskSize, "ask", "Ask", "Ask size", DomColumnSide.Ask, DomDataRequirement.Mbp, 64, DomColumnAlign.Right),
        new DomColumnDescriptor(DomColumnType.BidCumulative, "bidcum", "ΣBid", "Cumulative bid depth", DomColumnSide.Bid, DomDataRequirement.Mbp, 64, DomColumnAlign.Right),
        new DomColumnDescriptor(DomColumnType.AskCumulative, "askcum", "ΣAsk", "Cumulative ask depth", DomColumnSide.Ask, DomDataRequirement.Mbp, 64, DomColumnAlign.Right),
        new DomColumnDescriptor(DomColumnType.BidExecuted, "bidexec", "Exec", "Sell-aggressive executed volume", DomColumnSide.Bid, DomDataRequirement.Trades, 60, DomColumnAlign.Right),
        new DomColumnDescriptor(DomColumnType.AskExecuted, "askexec", "Exec", "Buy-aggressive executed volume", DomColumnSide.Ask, DomDataRequirement.Trades, 60, DomColumnAlign.Right),
        new DomColumnDescriptor(DomColumnType.Delta, "delta", "Δ", "Executed delta (buys − sells)", DomColumnSide.Center, DomDataRequirement.Trades, 56, DomColumnAlign.Right),
        new DomColumnDescriptor(DomColumnType.BidPullStack, "bidpull", "Pull/Stk", "Bid pulling / stacking", DomColumnSide.Bid, DomDataRequirement.Mbp, 70, DomColumnAlign.Right, Estimated: true),
        new DomColumnDescriptor(DomColumnType.AskPullStack, "askpull", "Pull/Stk", "Ask pulling / stacking", DomColumnSide.Ask, DomDataRequirement.Mbp, 70, DomColumnAlign.Right, Estimated: true),
        new DomColumnDescriptor(DomColumnType.BidRefill, "bidrefill", "Refill", "Bid replenishment count", DomColumnSide.Bid, DomDataRequirement.Mbp, 56, DomColumnAlign.Right, Estimated: true),
        new DomColumnDescriptor(DomColumnType.AskRefill, "askrefill", "Refill", "Ask replenishment count", DomColumnSide.Ask, DomDataRequirement.Mbp, 56, DomColumnAlign.Right, Estimated: true),
        new DomColumnDescriptor(DomColumnType.BidRelative, "bidrel", "Rel", "Bid relative size", DomColumnSide.Bid, DomDataRequirement.Mbp, 48, DomColumnAlign.Right),
        new DomColumnDescriptor(DomColumnType.AskRelative, "askrel", "Rel", "Ask relative size", DomColumnSide.Ask, DomDataRequirement.Mbp, 48, DomColumnAlign.Right),
        new DomColumnDescriptor(DomColumnType.MaxTrade, "maxtrade", "Max", "Largest trade at price", DomColumnSide.Center, DomDataRequirement.Trades, 56, DomColumnAlign.Right),
        new DomColumnDescriptor(DomColumnType.TradeCount, "trdcnt", "Trd", "Trade count at price", DomColumnSide.Center, DomDataRequirement.Trades, 48, DomColumnAlign.Right),
        new DomColumnDescriptor(DomColumnType.BidOrders, "bidord", "Ord", "Bid order count", DomColumnSide.Bid, DomDataRequirement.Mbo, 48, DomColumnAlign.Right),
        new DomColumnDescriptor(DomColumnType.AskOrders, "askord", "Ord", "Ask order count", DomColumnSide.Ask, DomDataRequirement.Mbo, 48, DomColumnAlign.Right),
        new DomColumnDescriptor(DomColumnType.BidLargestOrder, "bidlarge", "Lg", "Bid largest order", DomColumnSide.Bid, DomDataRequirement.Mbo, 52, DomColumnAlign.Right),
        new DomColumnDescriptor(DomColumnType.AskLargestOrder, "asklarge", "Lg", "Ask largest order", DomColumnSide.Ask, DomDataRequirement.Mbo, 52, DomColumnAlign.Right),
    };

    public static DomColumnDescriptor For(DomColumnType type)
    {
        foreach (var c in All) if (c.Type == type) return c;
        throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown DOM column type.");
    }

    /// <summary>Looks up a column by its stable serialisation id, or null if unknown.</summary>
    public static DomColumnDescriptor? ById(string id)
    {
        foreach (var c in All) if (string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase)) return c;
        return null;
    }

    public static bool RequiresMbo(DomColumnType type) => For(type).Requirement.HasFlag(DomDataRequirement.Mbo);
}

/// <summary>A named, ordered set of DOM columns. Built-ins are protected from overwrite.</summary>
public sealed record DomPreset(string Name, string Description, IReadOnlyList<DomColumnType> Columns, bool IsBuiltIn = false)
{
    /// <summary>True if any column needs native MBO data (so the UI can badge it / disable cleanly).</summary>
    public bool RequiresMbo => Columns.Any(DomColumnRegistry.RequiresMbo);
}

/// <summary>
/// The built-in read-only DOM presets — ten professional column layouts, all built
/// from the same canonical book/trade data. MBP presets work everywhere; the MBO
/// preset is capability-gated (its columns are labelled "MBO data required" when the
/// feed is market-by-price only). None of these add execution controls.
/// </summary>
public static class DomPresetRegistry
{
    private static DomPreset P(string name, string desc, params DomColumnType[] cols) => new(name, desc, cols, true);

    private const DomColumnType Price = DomColumnType.Price;

    public static IReadOnlyList<DomPreset> BuiltIns { get; } = new[]
    {
        P("Classic Depth", "Resting depth with cumulative columns and a clear inside market.",
            DomColumnType.BidCumulative, DomColumnType.BidSize, Price, DomColumnType.AskSize, DomColumnType.AskCumulative),

        P("Order Flow", "Executed volume beside resting size, with delta.",
            DomColumnType.BidExecuted, DomColumnType.BidSize, Price, DomColumnType.AskSize, DomColumnType.AskExecuted, DomColumnType.Delta),

        P("Pulling & Stacking", "Adds and pulls per side (estimated from MBP).",
            DomColumnType.BidPullStack, DomColumnType.BidSize, Price, DomColumnType.AskSize, DomColumnType.AskPullStack),

        P("Liquidity Analysis", "Cumulative + relative size with wall context.",
            DomColumnType.BidCumulative, DomColumnType.BidRelative, DomColumnType.BidSize, Price,
            DomColumnType.AskSize, DomColumnType.AskRelative, DomColumnType.AskCumulative),

        P("Executed Volume", "Sell/buy executed volume around the ladder, with delta and max trade.",
            DomColumnType.BidExecuted, DomColumnType.BidSize, Price, DomColumnType.AskSize, DomColumnType.AskExecuted,
            DomColumnType.Delta, DomColumnType.MaxTrade),

        P("Replenishment", "Refill counts per side (estimated from MBP).",
            DomColumnType.BidRefill, DomColumnType.BidSize, Price, DomColumnType.AskSize, DomColumnType.AskRefill),

        P("MBO Analytics", "Order-level columns — requires native MBO data.",
            DomColumnType.BidOrders, DomColumnType.BidLargestOrder, DomColumnType.BidSize, Price,
            DomColumnType.AskSize, DomColumnType.AskLargestOrder, DomColumnType.AskOrders),

        P("Compact Ladder", "Minimal bid / price / ask for a narrow side panel.",
            DomColumnType.BidSize, Price, DomColumnType.AskSize),

        P("Full Professional", "Cumulative, pull/stack, executed and resting size on both sides.",
            DomColumnType.BidCumulative, DomColumnType.BidPullStack, DomColumnType.BidExecuted, DomColumnType.BidSize,
            Price, DomColumnType.AskSize, DomColumnType.AskExecuted, DomColumnType.AskPullStack, DomColumnType.AskCumulative),

        P("Replay Study", "Executed + resting around price with delta and refills, for step-through review.",
            DomColumnType.BidExecuted, DomColumnType.BidSize, Price, DomColumnType.AskSize, DomColumnType.AskExecuted,
            DomColumnType.Delta, DomColumnType.BidRefill),
    };

    public static DomPreset Default => BuiltIns[0];

    public static DomPreset? ByName(string name)
    {
        foreach (var p in BuiltIns)
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p;
        return null;
    }
}
