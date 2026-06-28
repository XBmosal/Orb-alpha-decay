using FlowTerminal.Analytics.Bars;

namespace FlowTerminal.Analytics.PriceAction;

public enum GapDirection { None, Bullish, Bearish }

public readonly record struct FairValueGap(GapDirection Direction, long TopTicks, long BottomTicks, DateTime AtUtc)
{
    public long SizeTicks => TopTicks - BottomTicks;
}

/// <summary>
/// Fair Value Gap (FVG) identifier — a 3-bar price inefficiency:
///   Bullish FVG: bar[i-2].High &lt; bar[i].Low  → gap (bar[i-2].High, bar[i].Low)
///   Bearish FVG: bar[i-2].Low  &gt; bar[i].High → gap (bar[i].High, bar[i-2].Low)
/// The middle bar's move leaves an untraded band (a liquidity void). Descriptive;
/// no predictive guarantee.
/// </summary>
public sealed class FairValueGapDetector
{
    private Bar _b0, _b1;
    private int _count;
    private readonly long _minTicks;

    public FairValueGapDetector(long minTicks = 1) => _minTicks = Math.Max(1, minTicks);

    public FairValueGap? OnBar(in Bar bar)
    {
        FairValueGap? result = null;
        if (_count >= 2)
        {
            if (_b0.HighTicks < bar.LowTicks && bar.LowTicks - _b0.HighTicks >= _minTicks)
            {
                result = new FairValueGap(GapDirection.Bullish, bar.LowTicks, _b0.HighTicks, bar.EndUtc);
            }
            else if (_b0.LowTicks > bar.HighTicks && _b0.LowTicks - bar.HighTicks >= _minTicks)
            {
                result = new FairValueGap(GapDirection.Bearish, _b0.LowTicks, bar.HighTicks, bar.EndUtc);
            }
        }

        _b0 = _b1;
        _b1 = bar;
        if (_count < 2) _count++;
        return result;
    }
}
