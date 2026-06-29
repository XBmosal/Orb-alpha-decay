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
    long? OrbLowTicks,
    IReadOnlyList<FootprintColumn> Footprint,
    IReadOnlyList<TpoRow> Tpo)
{
    public static ChartOverlays Empty { get; } = new(
        Array.Empty<ProfileLevel>(), long.MinValue, long.MinValue, long.MinValue,
        Array.Empty<double>(), Array.Empty<FvgBox>(), null, null,
        Array.Empty<FootprintColumn>(), Array.Empty<TpoRow>());
}

/// <summary>A fair-value gap anchored to the bar index where it formed.</summary>
public readonly record struct FvgBox(int BarIndex, GapDirection Direction, long TopTicks, long BottomTicks);

/// <summary>Per-price bid/ask execution volume for one footprint bar.</summary>
public readonly record struct FootprintCell(long PriceTicks, long BidVolume, long AskVolume);

/// <summary>One footprint column: the bar plus its per-price bid/ask cells.</summary>
public sealed record FootprintColumn(
    long OpenTicks, long CloseTicks, long HighTicks, long LowTicks, IReadOnlyList<FootprintCell> Cells,
    DateTime StartUtc = default);

/// <summary>One TPO/Market-Profile row: a price and its lettered brackets (e.g. "ABD").</summary>
public readonly record struct TpoRow(long PriceTicks, string Letters);
