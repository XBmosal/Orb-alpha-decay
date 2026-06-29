namespace FlowTerminal.Analytics.Indicators;

/// <summary>The supported moving-average families.</summary>
public enum MovingAverageType
{
    /// <summary>Simple (arithmetic) moving average.</summary>
    Sma,

    /// <summary>Exponential moving average, alpha = 2/(period+1), seeded with the first SMA.</summary>
    Ema,

    /// <summary>Linearly weighted moving average (most recent value weighted highest).</summary>
    Wma,

    /// <summary>Wilder's smoothing / SMMA, alpha = 1/period, seeded with the first SMA.</summary>
    Rma,

    /// <summary>Hull moving average: WMA(2·WMA(n/2) − WMA(n)) over sqrt(n).</summary>
    Hma,
}

/// <summary>
/// A single incremental moving average over a scalar input series. All variants share
/// one warm-up rule: <see cref="IsReady"/> becomes true after <c>period</c> inputs and
/// <see cref="Value"/> is <see cref="double.NaN"/> before then. EMA and RMA are seeded
/// with the simple average of the first <c>period</c> values (the classic convention),
/// so results are deterministic and reproducible between live and replay.
///
/// Formulae:
///   SMA_t = (1/n) Σ x_{t-n+1..t}
///   EMA_t = α·x_t + (1−α)·EMA_{t-1},      α = 2/(n+1)
///   RMA_t = α·x_t + (1−α)·RMA_{t-1},      α = 1/n      (Wilder)
///   WMA_t = Σ w_i·x_i / Σ w_i,            w_i = 1..n (recent highest)
///   HMA_t = WMA_{√n}( 2·WMA_{n/2}(x) − WMA_{n}(x) )
/// </summary>
public sealed class MovingAverage : IScalarIndicator
{
    private readonly int _period;
    private readonly MovingAverageType _type;
    private readonly double _alpha;

    private readonly RollingWindow? _window; // SMA / WMA / seeding
    private double _ema = double.NaN;
    private int _seen;

    // HMA composition.
    private readonly MovingAverage? _wmaHalf;
    private readonly MovingAverage? _wmaFull;
    private readonly MovingAverage? _wmaSqrt;

    public MovingAverage(int period, MovingAverageType type)
    {
        if (period < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(period), period, "Period must be >= 1.");
        }

        _period = period;
        _type = type;

        if (type == MovingAverageType.Hma)
        {
            int half = Math.Max(1, period / 2);
            int sqrt = Math.Max(1, (int)Math.Round(Math.Sqrt(period)));
            _wmaHalf = new MovingAverage(half, MovingAverageType.Wma);
            _wmaFull = new MovingAverage(period, MovingAverageType.Wma);
            _wmaSqrt = new MovingAverage(sqrt, MovingAverageType.Wma);
        }
        else
        {
            _window = new RollingWindow(period);
            _alpha = type == MovingAverageType.Ema ? 2.0 / (period + 1) : 1.0 / period;
        }
    }

    public IndicatorDescriptor Descriptor { get; } = new(
        Id: "ma",
        Name: "Moving Average",
        Category: IndicatorCategory.Trend,
        Description: "Configurable moving average (SMA/EMA/WMA/RMA/HMA) over a selectable price source.",
        RequiredData: IndicatorData.Ohlc,
        Overlay: true,
        WarmupBars: 0);

    public int Period => _period;
    public MovingAverageType Type => _type;

    public double Value { get; private set; } = double.NaN;

    public bool IsReady => !double.IsNaN(Value);

    public void OnValue(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return; // ignore invalid input rather than poison the state
        }

        if (_type == MovingAverageType.Hma)
        {
            _wmaHalf!.OnValue(value);
            _wmaFull!.OnValue(value);
            if (_wmaHalf.IsReady && _wmaFull.IsReady)
            {
                _wmaSqrt!.OnValue(2.0 * _wmaHalf.Value - _wmaFull.Value);
                Value = _wmaSqrt.Value;
            }

            return;
        }

        var w = _window!;
        w.Push(value);
        _seen++;

        switch (_type)
        {
            case MovingAverageType.Sma:
                Value = w.IsFull ? w.Mean : double.NaN;
                break;

            case MovingAverageType.Wma:
                Value = w.IsFull ? WeightedMean(w) : double.NaN;
                break;

            case MovingAverageType.Ema:
            case MovingAverageType.Rma:
                if (_seen < _period)
                {
                    Value = double.NaN;
                }
                else if (_seen == _period)
                {
                    _ema = w.Mean;     // seed with the first SMA
                    Value = _ema;
                }
                else
                {
                    _ema = _alpha * value + (1 - _alpha) * _ema;
                    Value = _ema;
                }

                break;
        }
    }

    public void Reset()
    {
        _window?.Clear();
        _ema = double.NaN;
        _seen = 0;
        Value = double.NaN;
        _wmaHalf?.Reset();
        _wmaFull?.Reset();
        _wmaSqrt?.Reset();
    }

    private static double WeightedMean(RollingWindow w)
    {
        // Weights 1..n with the most recent value (highest index) weighted n.
        double num = 0;
        int n = w.Count;
        for (int i = 0; i < n; i++)
        {
            num += w[i] * (i + 1);
        }

        double denom = n * (n + 1) / 2.0;
        return num / denom;
    }
}
