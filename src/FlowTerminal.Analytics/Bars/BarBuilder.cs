using FlowTerminal.Domain.Events;

namespace FlowTerminal.Analytics.Bars;

/// <summary>
/// Mutable accumulator for a developing bar. Reused across bars to avoid per-bar
/// allocations on the hot path; <see cref="Snapshot"/> produces the immutable
/// <see cref="Bar"/> when the bar completes.
/// </summary>
public sealed class BarBuilder
{
    private bool _open;

    public BarKind Kind { get; }

    public BarBuilder(BarKind kind) => Kind = kind;

    public bool IsOpen => _open;
    public DateTime StartUtc { get; private set; }
    public DateTime EndUtc { get; private set; }
    public long OpenTicks { get; private set; }
    public long HighTicks { get; private set; }
    public long LowTicks { get; private set; }
    public long CloseTicks { get; private set; }
    public long Volume { get; private set; }
    public long BuyVolume { get; private set; }
    public long SellVolume { get; private set; }
    public int TradeCount { get; private set; }

    public long SpanTicks => _open ? HighTicks - LowTicks : 0;

    public void Begin(DateTime startUtc, long priceTicks)
    {
        _open = true;
        StartUtc = startUtc;
        EndUtc = startUtc;
        OpenTicks = HighTicks = LowTicks = CloseTicks = priceTicks;
        Volume = BuyVolume = SellVolume = 0;
        TradeCount = 0;
    }

    public void Add(in MarketEvent trade)
    {
        if (!_open)
        {
            Begin(trade.ExchangeTimestampUtc, trade.PriceTicks);
        }

        if (trade.PriceTicks > HighTicks)
        {
            HighTicks = trade.PriceTicks;
        }

        if (trade.PriceTicks < LowTicks)
        {
            LowTicks = trade.PriceTicks;
        }

        CloseTicks = trade.PriceTicks;
        EndUtc = trade.ExchangeTimestampUtc;
        Volume += trade.Quantity;
        TradeCount++;

        switch (trade.Aggressor)
        {
            case AggressorSide.Buy:
                BuyVolume += trade.Quantity;
                break;
            case AggressorSide.Sell:
                SellVolume += trade.Quantity;
                break;
        }
    }

    public Bar Snapshot(DateTime? endUtcOverride = null) => new(
        Kind, StartUtc, endUtcOverride ?? EndUtc,
        OpenTicks, HighTicks, LowTicks, CloseTicks,
        Volume, BuyVolume, SellVolume, TradeCount);

    public void Reset() => _open = false;
}
