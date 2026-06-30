using FlowTerminal.Domain.Events;

namespace FlowTerminal.Analytics.Vwap;

public readonly record struct VwapValue(double VwapTicks, double StdDevTicks)
{
    public double UpperBand(double k) => VwapTicks + k * StdDevTicks;
    public double LowerBand(double k) => VwapTicks - k * StdDevTicks;
}

/// <summary>
/// Volume-weighted average price with standard-deviation bands, computed
/// incrementally (O(1) per trade) from volume and price in ticks. Used for daily /
/// weekly / monthly / anchored VWAP — the only difference between them is when
/// <see cref="Reset"/> (the anchor) is called. The "developing" value is just a
/// query at the current accumulation.
///
/// variance = Σ(v·p²)/Σv − vwap²  (population, volume-weighted)
/// </summary>
public sealed class VwapCalculator
{
    private double _sumV;
    private double _sumPV;
    private double _sumP2V;

    public double TotalVolume => _sumV;

    public bool HasData => _sumV > 0;

    public void Add(in MarketEvent trade)
    {
        if (trade.Type != MarketEventType.Trade || trade.Quantity <= 0)
        {
            return;
        }

        AddAt(trade.PriceTicks, trade.Quantity);
    }

    public void AddAt(long priceTicks, long quantity)
    {
        if (quantity <= 0)
        {
            return;
        }

        double v = quantity;
        double p = priceTicks;
        _sumV += v;
        _sumPV += p * v;
        _sumP2V += p * p * v;
    }

    public VwapValue Value()
    {
        if (_sumV <= 0)
        {
            return new VwapValue(double.NaN, 0);
        }

        double vwap = _sumPV / _sumV;
        double variance = (_sumP2V / _sumV) - (vwap * vwap);
        if (variance < 0)
        {
            variance = 0; // guard against tiny negative from floating point
        }

        return new VwapValue(vwap, Math.Sqrt(variance));
    }

    /// <summary>Resets the anchor (start of a new daily/weekly/anchored VWAP).</summary>
    public void Reset()
    {
        _sumV = _sumPV = _sumP2V = 0;
    }
}
