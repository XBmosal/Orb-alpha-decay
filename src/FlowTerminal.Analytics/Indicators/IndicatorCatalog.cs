namespace FlowTerminal.Analytics.Indicators;

/// <summary>
/// Discovery and construction surface for the standard technical-indicator library.
/// Each entry pairs an <see cref="IndicatorDescriptor"/> (metadata for the Indicator
/// tab and capability badges) with a factory that builds the indicator with its
/// documented NQ/ES defaults. Order-flow studies that already ship (CVD, VWAP,
/// profiles, footprint, detectors) are catalogued separately via the existing
/// StudyCatalog; this catalogue covers the price/volume technical studies.
/// </summary>
public static class IndicatorCatalog
{
    public sealed record Entry(IndicatorDescriptor Descriptor, Func<IIndicator> Create);

    public static IReadOnlyList<Entry> All { get; } = Build();

    public static Entry? ById(string id)
    {
        foreach (var e in All)
        {
            if (string.Equals(e.Descriptor.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                return e;
            }
        }

        return null;
    }

    private static Entry[] Build()
    {
        Entry E(Func<IIndicator> create) { var i = create(); return new Entry(i.Descriptor, create); }

        return new[]
        {
            // Trend
            E(() => new MovingAverage(20, MovingAverageType.Ema)),
            E(() => new Macd()),
            E(() => new AverageDirectionalIndex()),

            // Volatility
            E(() => new AverageTrueRange()),
            E(() => new BollingerBands()),
            E(() => new DonchianChannel()),
            E(() => new KeltnerChannel()),

            // Momentum
            E(() => new Rsi()),
            E(() => new Stochastic()),
            E(() => new CommodityChannelIndex()),
            E(() => new RateOfChange()),
            E(() => new Momentum()),
        };
    }
}
