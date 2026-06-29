using FlowTerminal.Domain.Instruments;

namespace FlowTerminal.Analytics.Footprints;

/// <summary>
/// Which visual layouts each data mode supports, and a deterministic resolver that
/// snaps an incompatible (mode, layout) pair to the mode's default layout — so the UI
/// can offer only valid combinations and the renderer can never be asked to draw a
/// broken chart.
/// </summary>
public static class FootprintCompatibility
{
    public static FootprintVisualLayout DefaultLayout(FootprintMode mode) => mode switch
    {
        FootprintMode.BidAsk => FootprintVisualLayout.SplitText,
        FootprintMode.Delta => FootprintVisualLayout.MirroredHistogram,
        FootprintMode.VolumeProfile => FootprintVisualLayout.ProfileCandle,
        FootprintMode.DeltaPercent => FootprintVisualLayout.SingleValue,
        _ => FootprintVisualLayout.SingleValue,
    };

    public static IReadOnlyList<FootprintVisualLayout> AllowedLayouts(FootprintMode mode) => mode switch
    {
        FootprintMode.BidAsk => new[]
        {
            FootprintVisualLayout.SplitText, FootprintVisualLayout.SplitCell, FootprintVisualLayout.Histogram,
            FootprintVisualLayout.GradientCell, FootprintVisualLayout.TextOnly, FootprintVisualLayout.OutlineCell,
            FootprintVisualLayout.Ladder, FootprintVisualLayout.Marker, FootprintVisualLayout.Hybrid,
        },
        FootprintMode.Delta => new[]
        {
            FootprintVisualLayout.SingleValue, FootprintVisualLayout.Histogram, FootprintVisualLayout.MirroredHistogram,
            FootprintVisualLayout.GradientCell, FootprintVisualLayout.ProfileCandle, FootprintVisualLayout.TextOnly,
            FootprintVisualLayout.Marker, FootprintVisualLayout.Hybrid,
        },
        FootprintMode.TotalVolume => new[]
        {
            FootprintVisualLayout.SingleValue, FootprintVisualLayout.Histogram, FootprintVisualLayout.ProfileCandle,
            FootprintVisualLayout.GradientCell, FootprintVisualLayout.TextOnly, FootprintVisualLayout.Hybrid,
        },
        FootprintMode.BidOnly or FootprintMode.AskOnly => new[]
        {
            FootprintVisualLayout.SingleValue, FootprintVisualLayout.Histogram, FootprintVisualLayout.ProfileCandle,
            FootprintVisualLayout.GradientCell, FootprintVisualLayout.TextOnly,
        },
        FootprintMode.DeltaPercent => new[]
        {
            FootprintVisualLayout.SingleValue, FootprintVisualLayout.MirroredHistogram,
            FootprintVisualLayout.GradientCell, FootprintVisualLayout.TextOnly,
        },
        FootprintMode.TradeCount => new[]
        {
            FootprintVisualLayout.SingleValue, FootprintVisualLayout.Histogram,
            FootprintVisualLayout.ProfileCandle, FootprintVisualLayout.TextOnly,
        },
        FootprintMode.VolumeProfile => new[]
        {
            FootprintVisualLayout.ProfileCandle, FootprintVisualLayout.Histogram, FootprintVisualLayout.GradientCell,
        },
        _ => new[] { FootprintVisualLayout.SingleValue },
    };

    public static bool IsCompatible(FootprintMode mode, FootprintVisualLayout layout) =>
        AllowedLayouts(mode).Contains(layout);

    public static FootprintVisualLayout Resolve(FootprintMode mode, FootprintVisualLayout layout) =>
        IsCompatible(mode, layout) ? layout : DefaultLayout(mode);
}

/// <summary>A named footprint configuration. Built-ins are protected from overwrite.</summary>
public sealed record FootprintPreset(string Name, string Description, FootprintSettings Settings, bool IsBuiltIn = false)
{
    /// <summary>The settings for a specific instrument: visual fields kept, calc thresholds taken from the instrument preset.</summary>
    public FootprintSettings ForInstrument(RootSymbol root)
    {
        var inst = root == RootSymbol.ES ? FootprintSettings.Es : FootprintSettings.Nq;
        return (Settings with
        {
            LargeTradeThreshold = inst.LargeTradeThreshold,
            MinImbalanceVolume = inst.MinImbalanceVolume,
        }).Validate();
    }
}

/// <summary>
/// The built-in footprint visual presets — twelve distinct, professional configurations
/// that combine a data mode, a visual layout and overlay choices. All use the existing
/// green/light-purple palette; none introduce a new theme. Custom presets can be layered
/// on top by the workspace, but these are protected from accidental overwrite.
/// </summary>
public static class FootprintPresetRegistry
{
    private static FootprintSettings Base => FootprintSettings.Default;

    public static IReadOnlyList<FootprintPreset> BuiltIns { get; } = new[]
    {
        new FootprintPreset("Classic Bid×Ask", "Bid left, ask right, POC and bar delta — the readable default.",
            Base with { Mode = FootprintMode.BidAsk, VisualLayout = FootprintVisualLayout.SplitText }, true),

        new FootprintPreset("Bid×Ask Heat", "Split cells shaded by delta, with stacked zones and large trades.",
            Base with { Mode = FootprintMode.BidAsk, VisualLayout = FootprintVisualLayout.SplitCell,
                Background = FootprintBackground.Delta }, true),

        new FootprintPreset("Delta Footprint", "Centred signed delta with a delta-intensity background.",
            Base with { Mode = FootprintMode.Delta, VisualLayout = FootprintVisualLayout.SingleValue,
                Background = FootprintBackground.Delta }, true),

        new FootprintPreset("Delta Profile", "Zero-centred delta histogram: buys right, sells left.",
            Base with { Mode = FootprintMode.Delta, VisualLayout = FootprintVisualLayout.MirroredHistogram }, true),

        new FootprintPreset("Volume Profile", "Per-price total-volume profile with POC and a delta-tinted accent.",
            Base with { Mode = FootprintMode.VolumeProfile, VisualLayout = FootprintVisualLayout.ProfileCandle }, true),

        new FootprintPreset("Bid/Ask Profile", "Mirrored bid/ask bars (sells left, buys right) with imbalance outlines.",
            Base with { Mode = FootprintMode.BidAsk, VisualLayout = FootprintVisualLayout.Histogram }, true),

        new FootprintPreset("Minimal", "Text only, no cell fills — POC and imbalances by typography.",
            Base with { Mode = FootprintMode.BidAsk, VisualLayout = FootprintVisualLayout.TextOnly,
                ShowCandleBody = false }, true),

        new FootprintPreset("Order-Flow Ladder", "Dense bid | price | ask ladder with POC and imbalances.",
            Base with { Mode = FootprintMode.BidAsk, VisualLayout = FootprintVisualLayout.Ladder }, true),

        new FootprintPreset("Large Trades", "Subdued cells; large prints and max trade size emphasised.",
            Base with { Mode = FootprintMode.TotalVolume, VisualLayout = FootprintVisualLayout.GradientCell,
                Background = FootprintBackground.TotalVolume, SubdueOrdinaryCells = true }, true),

        new FootprintPreset("Imbalance Map", "Ordinary cells subdued; diagonal and stacked imbalances stand out.",
            Base with { Mode = FootprintMode.BidAsk, VisualLayout = FootprintVisualLayout.OutlineCell,
                SubdueOrdinaryCells = true }, true),

        new FootprintPreset("Volume + Delta Hybrid", "Total-volume profile with a delta value and delta accent.",
            Base with { Mode = FootprintMode.TotalVolume, VisualLayout = FootprintVisualLayout.Hybrid,
                Background = FootprintBackground.Delta }, true),

        new FootprintPreset("Clean Replay Study", "High-readability Bid×Ask for replay review and screenshots.",
            Base with { Mode = FootprintMode.BidAsk, VisualLayout = FootprintVisualLayout.SplitText,
                ShowDeltaFooter = true }, true),
    };

    public static FootprintPreset? ByName(string name)
    {
        foreach (var p in BuiltIns)
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p;
        return null;
    }

    public static FootprintPreset Default => BuiltIns[0];
}
