using FlowTerminal.Analytics.Bars;

namespace FlowTerminal.Analytics.Indicators;

/// <summary>
/// Moving Average Convergence/Divergence. MACD = EMA(fast) − EMA(slow); the signal
/// line is EMA(signal) of the MACD line; the histogram is MACD − signal. Standard
/// defaults 12/26/9. Warms up once the signal EMA is seeded. Confirmed on closes.
/// </summary>
public sealed class Macd : IScalarIndicator
{
    private readonly MovingAverage _fast;
    private readonly MovingAverage _slow;
    private readonly MovingAverage _signal;

    public Macd(int fast = 12, int slow = 26, int signal = 9)
    {
        if (fast < 1 || slow < 1 || signal < 1) throw new ArgumentOutOfRangeException(nameof(fast));
        if (fast >= slow) throw new ArgumentException("Fast period must be shorter than slow period.", nameof(fast));
        _fast = new MovingAverage(fast, MovingAverageType.Ema);
        _slow = new MovingAverage(slow, MovingAverageType.Ema);
        _signal = new MovingAverage(signal, MovingAverageType.Ema);
    }

    public IndicatorDescriptor Descriptor { get; } = new(
        "macd", "MACD", IndicatorCategory.Trend,
        "Difference of two EMAs with a signal line and histogram.",
        IndicatorData.Ohlc, Overlay: false, WarmupBars: 0);

    public double MacdLine { get; private set; } = double.NaN;
    public double Signal { get; private set; } = double.NaN;
    public double Histogram { get; private set; } = double.NaN;

    public double Value => MacdLine;
    public bool IsReady => !double.IsNaN(Signal);

    public void OnValue(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return;
        _fast.OnValue(value);
        _slow.OnValue(value);
        if (!_fast.IsReady || !_slow.IsReady) { MacdLine = Signal = Histogram = double.NaN; return; }

        MacdLine = _fast.Value - _slow.Value;
        _signal.OnValue(MacdLine);
        if (_signal.IsReady)
        {
            Signal = _signal.Value;
            Histogram = MacdLine - Signal;
        }
        else
        {
            Signal = Histogram = double.NaN;
        }
    }

    public void Reset()
    {
        _fast.Reset(); _slow.Reset(); _signal.Reset();
        MacdLine = Signal = Histogram = double.NaN;
    }
}

/// <summary>
/// Bollinger Bands: a middle SMA with upper/lower bands at ±k population standard
/// deviations. Defaults 20-period, k = 2. Bandwidth = (upper − lower)/middle. Warms up
/// after <c>period</c> values. Values are in ticks.
/// </summary>
public sealed class BollingerBands : IScalarIndicator
{
    private readonly RollingWindow _w;
    private readonly double _k;

    public BollingerBands(int period = 20, double k = 2.0)
    {
        if (period < 1) throw new ArgumentOutOfRangeException(nameof(period));
        if (k <= 0) throw new ArgumentOutOfRangeException(nameof(k));
        Period = period;
        _k = k;
        _w = new RollingWindow(period);
    }

    public int Period { get; }

    public IndicatorDescriptor Descriptor { get; } = new(
        "bbands", "Bollinger Bands", IndicatorCategory.Volatility,
        "SMA envelope at ±k standard deviations.",
        IndicatorData.Ohlc, Overlay: true, WarmupBars: 0);

    public double Middle { get; private set; } = double.NaN;
    public double Upper { get; private set; } = double.NaN;
    public double Lower { get; private set; } = double.NaN;
    public double Bandwidth { get; private set; } = double.NaN;

    public double Value => Middle;
    public bool IsReady => !double.IsNaN(Middle);

    public void OnValue(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return;
        _w.Push(value);
        if (!_w.IsFull) { Middle = Upper = Lower = Bandwidth = double.NaN; return; }

        Middle = _w.Mean;
        double sd = _w.PopulationStdDev;
        Upper = Middle + _k * sd;
        Lower = Middle - _k * sd;
        Bandwidth = Middle != 0 ? (Upper - Lower) / Middle : 0;
    }

    public void Reset() { _w.Clear(); Middle = Upper = Lower = Bandwidth = double.NaN; }
}

/// <summary>
/// Donchian Channel: highest high and lowest low over <c>period</c> bars, with the
/// midline as their average. A classic breakout/structure envelope. Warms up after
/// <c>period</c> bars. Values in ticks.
/// </summary>
public sealed class DonchianChannel : IBarIndicator
{
    private readonly RollingWindow _highs;
    private readonly RollingWindow _lows;

    public DonchianChannel(int period = 20)
    {
        if (period < 1) throw new ArgumentOutOfRangeException(nameof(period));
        Period = period;
        _highs = new RollingWindow(period);
        _lows = new RollingWindow(period);
    }

    public int Period { get; }

    public IndicatorDescriptor Descriptor { get; } = new(
        "donchian", "Donchian Channel", IndicatorCategory.Volatility,
        "Highest high / lowest low channel over a lookback.",
        IndicatorData.Ohlc, Overlay: true, WarmupBars: 0);

    public double Upper { get; private set; } = double.NaN;
    public double Lower { get; private set; } = double.NaN;
    public double Middle { get; private set; } = double.NaN;

    public double Value => Middle;
    public bool IsReady => !double.IsNaN(Middle);

    public void OnBar(in Bar bar)
    {
        _highs.Push(bar.HighTicks);
        _lows.Push(bar.LowTicks);
        if (!_highs.IsFull) { Upper = Lower = Middle = double.NaN; return; }
        Upper = _highs.Max();
        Lower = _lows.Min();
        Middle = (Upper + Lower) / 2.0;
    }

    public void Reset() { _highs.Clear(); _lows.Clear(); Upper = Lower = Middle = double.NaN; }
}

/// <summary>
/// Keltner Channel: an EMA midline with bands at ±multiplier·ATR. Defaults 20-period
/// EMA, 10-period ATR, multiplier 2. Warms up once both the EMA and ATR are ready.
/// Values in ticks.
/// </summary>
public sealed class KeltnerChannel : IBarIndicator
{
    private readonly MovingAverage _mid;
    private readonly AverageTrueRange _atr;
    private readonly PriceSource _source;
    private readonly double _mult;

    public KeltnerChannel(int emaPeriod = 20, int atrPeriod = 10, double multiplier = 2.0,
        PriceSource source = PriceSource.Close)
    {
        if (emaPeriod < 1 || atrPeriod < 1) throw new ArgumentOutOfRangeException(nameof(emaPeriod));
        if (multiplier <= 0) throw new ArgumentOutOfRangeException(nameof(multiplier));
        _mid = new MovingAverage(emaPeriod, MovingAverageType.Ema);
        _atr = new AverageTrueRange(atrPeriod);
        _source = source;
        _mult = multiplier;
    }

    public IndicatorDescriptor Descriptor { get; } = new(
        "keltner", "Keltner Channel", IndicatorCategory.Volatility,
        "EMA midline with ATR-scaled bands.",
        IndicatorData.Ohlc, Overlay: true, WarmupBars: 0);

    public double Middle { get; private set; } = double.NaN;
    public double Upper { get; private set; } = double.NaN;
    public double Lower { get; private set; } = double.NaN;

    public double Value => Middle;
    public bool IsReady => !double.IsNaN(Upper);

    public void OnBar(in Bar bar)
    {
        _mid.OnValue(_source.Select(bar));
        _atr.OnBar(bar);
        if (!_mid.IsReady || !_atr.IsReady) { Middle = Upper = Lower = double.NaN; return; }
        Middle = _mid.Value;
        Upper = Middle + _mult * _atr.Value;
        Lower = Middle - _mult * _atr.Value;
    }

    public void Reset() { _mid.Reset(); _atr.Reset(); Middle = Upper = Lower = double.NaN; }
}
