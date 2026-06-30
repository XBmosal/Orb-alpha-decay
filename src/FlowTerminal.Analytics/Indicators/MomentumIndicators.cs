using FlowTerminal.Analytics.Bars;

namespace FlowTerminal.Analytics.Indicators;

/// <summary>
/// Wilder's Relative Strength Index. Average gains and losses are smoothed with
/// Wilder's RMA (alpha = 1/period, seeded with the first SMA of the first
/// <c>period</c> deltas). RSI = 100 − 100/(1 + avgGain/avgLoss), clamped to [0, 100].
/// When average loss is zero RSI is defined as 100 (no division by zero).
/// Requires <c>period + 1</c> values to warm up. Confirmed (non-repainting) on closes.
/// </summary>
public sealed class Rsi : IScalarIndicator
{
    private readonly MovingAverage _avgGain;
    private readonly MovingAverage _avgLoss;
    private double _prev = double.NaN;

    public Rsi(int period = 14)
    {
        if (period < 1) throw new ArgumentOutOfRangeException(nameof(period));
        Period = period;
        _avgGain = new MovingAverage(period, MovingAverageType.Rma);
        _avgLoss = new MovingAverage(period, MovingAverageType.Rma);
    }

    public int Period { get; }

    public IndicatorDescriptor Descriptor { get; } = new(
        "rsi", "RSI", IndicatorCategory.Momentum,
        "Wilder's Relative Strength Index — momentum oscillator bounded 0–100.",
        IndicatorData.Ohlc, Overlay: false, WarmupBars: 0);

    public double Value { get; private set; } = double.NaN;

    public bool IsReady => !double.IsNaN(Value);

    public void OnValue(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return;
        if (double.IsNaN(_prev)) { _prev = value; return; }

        double change = value - _prev;
        _prev = value;
        _avgGain.OnValue(Math.Max(0.0, change));
        _avgLoss.OnValue(Math.Max(0.0, -change));

        if (!_avgGain.IsReady || !_avgLoss.IsReady) { Value = double.NaN; return; }

        double loss = _avgLoss.Value;
        double rsi = loss <= 0 ? 100.0 : 100.0 - 100.0 / (1.0 + _avgGain.Value / loss);
        Value = Math.Clamp(rsi, 0.0, 100.0);
    }

    public void Reset()
    {
        _avgGain.Reset();
        _avgLoss.Reset();
        _prev = double.NaN;
        Value = double.NaN;
    }
}

/// <summary>
/// Rate of Change: 100·(price − price[n]) / price[n]. Returns 0 when the lagged price
/// is 0 (avoids division by zero). Warms up after <c>period + 1</c> values.
/// </summary>
public sealed class RateOfChange : IScalarIndicator
{
    private readonly RollingWindow _w;

    public RateOfChange(int period = 9)
    {
        if (period < 1) throw new ArgumentOutOfRangeException(nameof(period));
        Period = period;
        _w = new RollingWindow(period + 1);
    }

    public int Period { get; }

    public IndicatorDescriptor Descriptor { get; } = new(
        "roc", "Rate of Change", IndicatorCategory.Momentum,
        "Percentage change of price over a fixed lookback.",
        IndicatorData.Ohlc, Overlay: false, WarmupBars: 0);

    public double Value { get; private set; } = double.NaN;
    public bool IsReady => !double.IsNaN(Value);

    public void OnValue(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return;
        _w.Push(value);
        if (!_w.IsFull) { Value = double.NaN; return; }
        double past = _w.Oldest;
        Value = past == 0 ? 0.0 : 100.0 * (value - past) / past;
    }

    public void Reset() { _w.Clear(); Value = double.NaN; }
}

/// <summary>Momentum: price − price[n]. Warms up after <c>period + 1</c> values.</summary>
public sealed class Momentum : IScalarIndicator
{
    private readonly RollingWindow _w;

    public Momentum(int period = 10)
    {
        if (period < 1) throw new ArgumentOutOfRangeException(nameof(period));
        Period = period;
        _w = new RollingWindow(period + 1);
    }

    public int Period { get; }

    public IndicatorDescriptor Descriptor { get; } = new(
        "momentum", "Momentum", IndicatorCategory.Momentum,
        "Absolute price change over a fixed lookback.",
        IndicatorData.Ohlc, Overlay: false, WarmupBars: 0);

    public double Value { get; private set; } = double.NaN;
    public bool IsReady => !double.IsNaN(Value);

    public void OnValue(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return;
        _w.Push(value);
        Value = _w.IsFull ? value - _w.Oldest : double.NaN;
    }

    public void Reset() { _w.Clear(); Value = double.NaN; }
}

/// <summary>
/// Stochastic Oscillator. Raw %K = 100·(close − lowest_n) / (highest_n − lowest_n);
/// %K is the <c>kSmooth</c>-period SMA of raw %K (slow stochastic) and %D is the
/// <c>dPeriod</c>-period SMA of %K. When the lookback range is flat, raw %K is defined
/// as 50 (neutral) to avoid division by zero. Bounded 0–100.
/// </summary>
public sealed class Stochastic : IBarIndicator
{
    private readonly RollingWindow _highs;
    private readonly RollingWindow _lows;
    private readonly MovingAverage _k;
    private readonly MovingAverage _d;

    public Stochastic(int period = 14, int kSmooth = 3, int dPeriod = 3)
    {
        if (period < 1 || kSmooth < 1 || dPeriod < 1) throw new ArgumentOutOfRangeException(nameof(period));
        Period = period;
        _highs = new RollingWindow(period);
        _lows = new RollingWindow(period);
        _k = new MovingAverage(kSmooth, MovingAverageType.Sma);
        _d = new MovingAverage(dPeriod, MovingAverageType.Sma);
    }

    public int Period { get; }

    public IndicatorDescriptor Descriptor { get; } = new(
        "stoch", "Stochastic Oscillator", IndicatorCategory.Momentum,
        "Position of close within the recent high-low range; %K and %D lines (0–100).",
        IndicatorData.Ohlc, Overlay: false, WarmupBars: 0);

    public double K { get; private set; } = double.NaN;
    public double D { get; private set; } = double.NaN;

    public bool IsReady => !double.IsNaN(D);

    public void OnBar(in Bar bar)
    {
        _highs.Push(bar.HighTicks);
        _lows.Push(bar.LowTicks);
        if (!_highs.IsFull) { K = double.NaN; D = double.NaN; return; }

        double hi = _highs.Max(), lo = _lows.Min();
        double range = hi - lo;
        double rawK = range <= 0 ? 50.0 : 100.0 * (bar.CloseTicks - lo) / range;
        _k.OnValue(Math.Clamp(rawK, 0.0, 100.0));
        if (!_k.IsReady) { K = double.NaN; D = double.NaN; return; }

        K = _k.Value;
        _d.OnValue(K);
        D = _d.IsReady ? _d.Value : double.NaN;
    }

    public void Reset()
    {
        _highs.Clear();
        _lows.Clear();
        _k.Reset();
        _d.Reset();
        K = double.NaN;
        D = double.NaN;
    }
}

/// <summary>
/// Commodity Channel Index: (TP − SMA(TP)) / (0.015 · meanAbsDev(TP)), where
/// TP = (H+L+C)/3. The 0.015 constant scales ~70–80% of values into ±100. Returns 0
/// when mean absolute deviation is 0. Warms up after <c>period</c> bars.
/// </summary>
public sealed class CommodityChannelIndex : IBarIndicator
{
    private readonly RollingWindow _tp;

    public CommodityChannelIndex(int period = 20)
    {
        if (period < 1) throw new ArgumentOutOfRangeException(nameof(period));
        Period = period;
        _tp = new RollingWindow(period);
    }

    public int Period { get; }

    public IndicatorDescriptor Descriptor { get; } = new(
        "cci", "Commodity Channel Index", IndicatorCategory.Momentum,
        "Deviation of typical price from its moving average, scaled by mean deviation.",
        IndicatorData.Ohlc, Overlay: false, WarmupBars: 0);

    public double Value { get; private set; } = double.NaN;
    public bool IsReady => !double.IsNaN(Value);

    public void OnBar(in Bar bar)
    {
        double tp = (bar.HighTicks + bar.LowTicks + bar.CloseTicks) / 3.0;
        _tp.Push(tp);
        if (!_tp.IsFull) { Value = double.NaN; return; }

        double md = _tp.MeanAbsoluteDeviation();
        Value = md <= 0 ? 0.0 : (tp - _tp.Mean) / (0.015 * md);
    }

    public void Reset() { _tp.Clear(); Value = double.NaN; }
}
