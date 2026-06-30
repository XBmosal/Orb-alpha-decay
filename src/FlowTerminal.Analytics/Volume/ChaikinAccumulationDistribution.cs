using FlowTerminal.Analytics.Bars;

namespace FlowTerminal.Analytics.Volume;

/// <summary>
/// Chaikin Accumulation/Distribution line — a volume-weighted pressure proxy:
///   MFM = ((Close − Low) − (High − Close)) / (High − Low)   (0 when High == Low)
///   ADL += MFM × Volume
/// A rising line suggests accumulation, falling suggests distribution. Computed
/// incrementally per completed bar; integer-tick prices.
/// </summary>
public sealed class ChaikinAccumulationDistribution
{
    public double Value { get; private set; }

    public double OnBar(in Bar bar)
    {
        long range = bar.HighTicks - bar.LowTicks;
        if (range > 0 && bar.Volume > 0)
        {
            double mfm = ((bar.CloseTicks - bar.LowTicks) - (bar.HighTicks - bar.CloseTicks)) / (double)range;
            Value += mfm * bar.Volume;
        }

        return Value;
    }

    public void Reset() => Value = 0;
}
