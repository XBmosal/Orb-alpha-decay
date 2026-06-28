using FlowTerminal.Analytics.PriceAction;
using FlowTerminal.Analytics.Profiles;

namespace FlowTerminal.Charting.Overlays;

/// <summary>
/// Per-frame study overlay data computed by the feed and drawn onto the price chart
/// by <see cref="ChartOverlayRenderer"/>. All prices are integer ticks; series are
/// aligned to the visible bar list by index.
/// </summary>
public sealed record ChartOverlays(
    IReadOnlyList<ProfileLevel> Profile,
    long PocTicks,
    long VahTicks,
    long ValTicks,
    IReadOnlyList<double> VwapByBar,
    IReadOnlyList<FvgBox> Fvgs,
    long? OrbHighTicks,
    long? OrbLowTicks)
{
    public static ChartOverlays Empty { get; } = new(
        Array.Empty<ProfileLevel>(), long.MinValue, long.MinValue, long.MinValue,
        Array.Empty<double>(), Array.Empty<FvgBox>(), null, null);
}

/// <summary>A fair-value gap anchored to the bar index where it formed.</summary>
public readonly record struct FvgBox(int BarIndex, GapDirection Direction, long TopTicks, long BottomTicks);
