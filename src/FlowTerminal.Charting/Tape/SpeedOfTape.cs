using FlowTerminal.Domain.Events;

namespace FlowTerminal.Charting.Tape;

public readonly record struct TapeSpeed(
    double TradesPerSecondShort,
    double ContractsPerSecondShort,
    double TradesPerSecondLong,
    double AccelerationRatio);

/// <summary>
/// Speed-of-tape metrics over a short activity window vs a long baseline window,
/// using exchange timestamps so the figures are identical in live and replay. The
/// acceleration ratio is short-window trade rate ÷ long-window baseline rate
/// (1.0 = normal, &gt;1 = accelerating). Bounded memory: entries older than the
/// long window are evicted.
/// </summary>
public sealed class SpeedOfTape
{
    private readonly TimeSpan _shortWindow;
    private readonly TimeSpan _longWindow;
    private readonly Queue<(DateTime Ts, long Qty)> _window = new();

    public SpeedOfTape(TimeSpan? shortWindow = null, TimeSpan? longWindow = null)
    {
        _shortWindow = shortWindow ?? TimeSpan.FromSeconds(5);
        _longWindow = longWindow ?? TimeSpan.FromSeconds(60);
        if (_longWindow <= _shortWindow)
        {
            throw new ArgumentException("Long window must exceed short window.");
        }
    }

    public void Add(in MarketEvent trade)
    {
        if (trade.Type != MarketEventType.Trade || trade.Quantity <= 0)
        {
            return;
        }

        _window.Enqueue((trade.ExchangeTimestampUtc, trade.Quantity));
        Evict(trade.ExchangeTimestampUtc);
    }

    public TapeSpeed Compute(DateTime nowUtc)
    {
        Evict(nowUtc);

        long shortTrades = 0, shortQty = 0, longTrades = 0;
        DateTime shortFrom = nowUtc - _shortWindow;

        foreach (var (ts, qty) in _window)
        {
            longTrades++;
            if (ts >= shortFrom)
            {
                shortTrades++;
                shortQty += qty;
            }
        }

        double shortSecs = _shortWindow.TotalSeconds;
        double longSecs = _longWindow.TotalSeconds;
        double shortRate = shortTrades / shortSecs;
        double longRate = longTrades / longSecs;
        double accel = longRate > 0 ? shortRate / longRate : 0;

        return new TapeSpeed(shortRate, shortQty / shortSecs, longRate, accel);
    }

    private void Evict(DateTime nowUtc)
    {
        DateTime cutoff = nowUtc - _longWindow;
        while (_window.Count > 0 && _window.Peek().Ts < cutoff)
        {
            _window.Dequeue();
        }
    }
}
