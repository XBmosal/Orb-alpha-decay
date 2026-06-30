namespace FlowTerminal.Analytics.PriceAction;

/// <summary>
/// Detects an opening/overnight/weekend price gap: the session open relative to the
/// prior session close. Prices are integer ticks. Gaps below <paramref name="minTicks"/>
/// are ignored.
/// </summary>
public static class GapDetector
{
    public static (GapDirection Direction, long SizeTicks) Classify(long prevCloseTicks, long openTicks, long minTicks = 1)
    {
        long diff = openTicks - prevCloseTicks;
        if (Math.Abs(diff) < Math.Max(1, minTicks)) return (GapDirection.None, 0);
        return diff > 0 ? (GapDirection.Bullish, diff) : (GapDirection.Bearish, -diff);
    }
}

/// <summary>
/// Average Daily Range (ADR): the mean of recent daily ranges (high − low) in ticks,
/// used to project anticipated session extensions from the session open.
/// </summary>
public sealed class AverageDailyRange
{
    private readonly int _period;
    private readonly Queue<long> _ranges = new();
    private long _sum;

    public AverageDailyRange(int period = 14)
    {
        if (period < 1) throw new ArgumentOutOfRangeException(nameof(period));
        _period = period;
    }

    public bool HasValue => _ranges.Count > 0;

    public void AddDay(long highTicks, long lowTicks)
    {
        long range = Math.Max(0, highTicks - lowTicks);
        _ranges.Enqueue(range);
        _sum += range;
        while (_ranges.Count > _period) _sum -= _ranges.Dequeue();
    }

    public double AverageTicks => _ranges.Count == 0 ? 0 : _sum / (double)_ranges.Count;

    /// <summary>Projected upper/lower ADR targets measured from the session open (ticks).</summary>
    public (long UpTarget, long DownTarget) TargetsFromOpen(long openTicks)
    {
        long half = (long)Math.Round(AverageTicks / 2.0);
        return (openTicks + half, openTicks - half);
    }
}

/// <summary>
/// Opening Range Breakout (ORB) structure: tracks the high/low established during the
/// opening range window, then flags the first break above/below it.
/// </summary>
public sealed class OpeningRangeBreakout
{
    private readonly DateTime _rangeEndUtc;
    private long _high = long.MinValue;
    private long _low = long.MaxValue;
    private bool _established;
    private GapDirection _broken = GapDirection.None;

    public OpeningRangeBreakout(DateTime rangeEndUtc) => _rangeEndUtc = rangeEndUtc;

    public long HighTicks => _high;
    public long LowTicks => _low;
    public bool IsEstablished => _established;
    public GapDirection BreakoutDirection => _broken;

    /// <summary>Feeds a trade (price ticks + time). Returns a breakout direction the first time price breaks the range.</summary>
    public GapDirection OnTrade(long priceTicks, DateTime utc)
    {
        if (utc < _rangeEndUtc)
        {
            _high = Math.Max(_high, priceTicks);
            _low = Math.Min(_low, priceTicks);
            return GapDirection.None;
        }

        if (!_established)
        {
            _established = _high > long.MinValue && _low < long.MaxValue;
        }

        if (_established && _broken == GapDirection.None)
        {
            if (priceTicks > _high) _broken = GapDirection.Bullish;
            else if (priceTicks < _low) _broken = GapDirection.Bearish;
            return _broken;
        }

        return GapDirection.None;
    }
}
