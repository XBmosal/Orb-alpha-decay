namespace FlowTerminal.Analytics.Bars;

public enum BarKind
{
    Time,
    Tick,
    Volume,
    Range,
}

/// <summary>
/// An immutable completed (or snapshotted) bar. Prices are integer ticks; time is
/// UTC. Buy/sell volume are split by trade aggressor; trades with an unknown
/// aggressor contribute to <see cref="Volume"/> but not to buy/sell or delta.
/// </summary>
public readonly record struct Bar(
    BarKind Kind,
    DateTime StartUtc,
    DateTime EndUtc,
    long OpenTicks,
    long HighTicks,
    long LowTicks,
    long CloseTicks,
    long Volume,
    long BuyVolume,
    long SellVolume,
    int TradeCount)
{
    /// <summary>Bar delta = aggressive buy volume − aggressive sell volume.</summary>
    public long Delta => BuyVolume - SellVolume;
}
