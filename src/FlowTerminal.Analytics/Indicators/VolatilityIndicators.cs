using FlowTerminal.Analytics.Bars;

namespace FlowTerminal.Analytics.Indicators;

/// <summary>
/// True Range and Wilder's Average True Range (in ticks). TR = max(H−L, |H−C_prev|,
/// |L−C_prev|); the first bar's TR is H−L. ATR is the RMA (alpha = 1/period) of TR,
/// seeded with the first SMA. Warms up after <c>period</c> bars.
/// </summary>
public sealed class AverageTrueRange : IBarIndicator
{
    private readonly MovingAverage _rma;
    private double _prevClose = double.NaN;

    public AverageTrueRange(int period = 14)
    {
        if (period < 1) throw new ArgumentOutOfRangeException(nameof(period));
        Period = period;
        _rma = new MovingAverage(period, MovingAverageType.Rma);
    }

    public int Period { get; }

    public IndicatorDescriptor Descriptor { get; } = new(
        "atr", "Average True Range", IndicatorCategory.Volatility,
        "Wilder's average of the true range; volatility in ticks.",
        IndicatorData.Ohlc, Overlay: false, WarmupBars: 0);

    public double TrueRange { get; private set; } = double.NaN;
    public double Value { get; private set; } = double.NaN;
    public bool IsReady => !double.IsNaN(Value);

    public void OnBar(in Bar bar)
    {
        double h = bar.HighTicks, l = bar.LowTicks;
        double tr = double.IsNaN(_prevClose)
            ? h - l
            : Math.Max(h - l, Math.Max(Math.Abs(h - _prevClose), Math.Abs(l - _prevClose)));
        TrueRange = tr;
        _prevClose = bar.CloseTicks;
        _rma.OnValue(tr);
        Value = _rma.Value;
    }

    public void Reset()
    {
        _rma.Reset();
        _prevClose = double.NaN;
        TrueRange = double.NaN;
        Value = double.NaN;
    }
}

/// <summary>
/// Wilder's Average Directional Index with the directional indicators +DI and −DI.
/// Directional movement and true range are Wilder-smoothed (RMA, alpha = 1/period);
/// DX = 100·|+DI − −DI|/(+DI + −DI) and ADX is the RMA of DX. ADX is bounded 0–100 and
/// warms up after roughly 2·period bars. Division guards keep it free of NaN/∞.
/// </summary>
public sealed class AverageDirectionalIndex : IBarIndicator
{
    private readonly MovingAverage _trRma;
    private readonly MovingAverage _plusRma;
    private readonly MovingAverage _minusRma;
    private readonly MovingAverage _dxRma;
    private double _prevHigh = double.NaN, _prevLow = double.NaN, _prevClose = double.NaN;

    public AverageDirectionalIndex(int period = 14)
    {
        if (period < 1) throw new ArgumentOutOfRangeException(nameof(period));
        Period = period;
        _trRma = new MovingAverage(period, MovingAverageType.Rma);
        _plusRma = new MovingAverage(period, MovingAverageType.Rma);
        _minusRma = new MovingAverage(period, MovingAverageType.Rma);
        _dxRma = new MovingAverage(period, MovingAverageType.Rma);
    }

    public int Period { get; }

    public IndicatorDescriptor Descriptor { get; } = new(
        "adx", "Average Directional Index", IndicatorCategory.Trend,
        "Wilder's ADX with +DI/−DI; trend-strength oscillator (0–100).",
        IndicatorData.Ohlc, Overlay: false, WarmupBars: 0);

    public double PlusDi { get; private set; } = double.NaN;
    public double MinusDi { get; private set; } = double.NaN;
    public double Value { get; private set; } = double.NaN; // ADX
    public bool IsReady => !double.IsNaN(Value);

    public void OnBar(in Bar bar)
    {
        double h = bar.HighTicks, l = bar.LowTicks;
        if (double.IsNaN(_prevHigh))
        {
            _prevHigh = h; _prevLow = l; _prevClose = bar.CloseTicks;
            return; // need a previous bar for directional movement
        }

        double up = h - _prevHigh;
        double down = _prevLow - l;
        double plusDm = up > down && up > 0 ? up : 0;
        double minusDm = down > up && down > 0 ? down : 0;
        double tr = Math.Max(h - l, Math.Max(Math.Abs(h - _prevClose), Math.Abs(l - _prevClose)));

        _prevHigh = h; _prevLow = l; _prevClose = bar.CloseTicks;

        _trRma.OnValue(tr);
        _plusRma.OnValue(plusDm);
        _minusRma.OnValue(minusDm);

        if (!_trRma.IsReady) { PlusDi = MinusDi = double.NaN; return; }

        double atr = _trRma.Value;
        PlusDi = atr <= 0 ? 0 : 100.0 * _plusRma.Value / atr;
        MinusDi = atr <= 0 ? 0 : 100.0 * _minusRma.Value / atr;

        double sum = PlusDi + MinusDi;
        double dx = sum <= 0 ? 0 : 100.0 * Math.Abs(PlusDi - MinusDi) / sum;
        _dxRma.OnValue(dx);
        Value = _dxRma.IsReady ? Math.Clamp(_dxRma.Value, 0.0, 100.0) : double.NaN;
    }

    public void Reset()
    {
        _trRma.Reset(); _plusRma.Reset(); _minusRma.Reset(); _dxRma.Reset();
        _prevHigh = _prevLow = _prevClose = double.NaN;
        PlusDi = MinusDi = Value = double.NaN;
    }
}
