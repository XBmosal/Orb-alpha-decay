using FlowTerminal.Domain.Events;

namespace FlowTerminal.Analytics.BigTrades;

/// <summary>The outcome of testing a candidate quantity against the active threshold.</summary>
public readonly record struct ThresholdResult(
    bool IsLarge, long ThresholdUsed, double PercentileRank, double ZScore, bool WarmedUp);

/// <summary>
/// Bounded, streaming statistics over recent trade sizes: count, mean, population
/// standard deviation, and an exact percentile (sorted-window). One structure backs the
/// rolling and session distributions; <see cref="Reset"/> clears it for a new session.
/// Memory is O(capacity); there is no per-trade allocation on the hot path.
/// </summary>
internal sealed class RollingSizeStats
{
    private readonly long[] _ring;
    private readonly long[] _sortBuffer;
    private int _head;
    private int _count;
    private double _sum;
    private double _sumSq;

    public RollingSizeStats(int capacity)
    {
        capacity = Math.Max(1, capacity);
        _ring = new long[capacity];
        _sortBuffer = new long[capacity];
    }

    public int Count => _count;
    public double Mean => _count > 0 ? _sum / _count : 0;

    public double StdDev
    {
        get
        {
            if (_count < 2) return 0;
            double variance = (_sumSq - _sum * _sum / _count) / _count;
            return variance > 0 ? Math.Sqrt(variance) : 0;
        }
    }

    public void Push(long size)
    {
        if (_count == _ring.Length)
        {
            long evicted = _ring[_head];
            _sum -= evicted;
            _sumSq -= (double)evicted * evicted;
        }
        else
        {
            _count++;
        }

        _ring[_head] = size;
        _sum += size;
        _sumSq += (double)size * size;
        _head = (_head + 1) % _ring.Length;
    }

    /// <summary>Exact percentile of the current window (p in 0..1). 0 when empty.</summary>
    public long Percentile(double p)
    {
        if (_count == 0) return 0;
        // Valid entries occupy [0.._count): contiguous when filling, the whole ring when
        // full. Order is irrelevant because we sort.
        Array.Copy(_ring, 0, _sortBuffer, 0, _count);
        Array.Sort(_sortBuffer, 0, _count);
        int idx = (int)Math.Clamp(Math.Ceiling(p * _count) - 1, 0, _count - 1);
        return _sortBuffer[idx];
    }

    /// <summary>Fraction of the window strictly less than <paramref name="value"/> (0..1).</summary>
    public double RankOf(long value)
    {
        if (_count == 0) return double.NaN;
        int below = 0;
        for (int i = 0; i < _count; i++) if (_ring[i] < value) below++;
        return below / (double)_count;
    }

    public void Reset()
    {
        _head = 0;
        _count = 0;
        _sum = 0;
        _sumSq = 0;
    }
}

/// <summary>
/// Computes whether a candidate quantity is "big" under the configured
/// <see cref="ThresholdMode"/>, maintaining the rolling/session distribution of
/// individual trade sizes it is fed. Deterministic and single-writer, so replay
/// reproduces identical thresholds. Every result reports the threshold it used and the
/// candidate's percentile rank so the decision is fully transparent.
/// </summary>
public sealed class TradeThresholdEngine
{
    private readonly BigTradeSettings _settings;
    private readonly RollingSizeStats _rolling;
    private readonly RollingSizeStats _session;

    public TradeThresholdEngine(BigTradeSettings settings)
    {
        _settings = settings.Validate();
        _rolling = new RollingSizeStats(_settings.RollingWindow);
        _session = new RollingSizeStats(_settings.SessionCapacity);
    }

    /// <summary>Feeds an individual trade size into the distributions (every trade, not just big ones).</summary>
    public void Observe(long size)
    {
        if (size <= 0) return;
        _rolling.Push(size);
        _session.Push(size);
    }

    public void ResetSession() => _session.Reset();

    public void Reset()
    {
        _rolling.Reset();
        _session.Reset();
    }

    /// <summary>
    /// Tests a candidate total (a single print or an aggregated group's total) for a side.
    /// The absolute floor always applies; below the minimum sample count adaptive modes
    /// report "not warmed up" and fall back to the absolute floor only.
    /// </summary>
    public ThresholdResult Evaluate(long candidate, AggressorSide side)
    {
        long floor = _settings.AbsoluteFloor;
        double rank = _rolling.Count > 0 ? _rolling.RankOf(candidate) : double.NaN;

        switch (_settings.Mode)
        {
            case ThresholdMode.Fixed:
            {
                long t = Math.Max(floor, _settings.FixedThresholdFor(side));
                return new ThresholdResult(candidate >= t, t, rank, double.NaN, WarmedUp: true);
            }

            case ThresholdMode.ZScore:
            {
                if (_rolling.Count < _settings.MinSamples)
                    return new ThresholdResult(candidate >= Math.Max(floor, FixedFallback(side)), Math.Max(floor, FixedFallback(side)), rank, double.NaN, WarmedUp: false);
                double sd = _rolling.StdDev;
                double mean = _rolling.Mean;
                long t = Math.Max(floor, (long)Math.Ceiling(mean + _settings.ZScoreThreshold * sd));
                double z = sd > 0 ? (candidate - mean) / sd : 0;
                return new ThresholdResult(candidate >= t, t, rank, z, WarmedUp: true);
            }

            case ThresholdMode.RelativeToAverage:
            {
                if (_rolling.Count < _settings.MinSamples)
                    return new ThresholdResult(candidate >= Math.Max(floor, FixedFallback(side)), Math.Max(floor, FixedFallback(side)), rank, double.NaN, WarmedUp: false);
                long t = Math.Max(floor, (long)Math.Ceiling(_settings.RelativeMultiplier * _rolling.Mean));
                return new ThresholdResult(candidate >= t, t, rank, double.NaN, WarmedUp: true);
            }

            case ThresholdMode.SessionPercentile:
            {
                if (_session.Count < _settings.MinSamples)
                    return new ThresholdResult(candidate >= Math.Max(floor, FixedFallback(side)), Math.Max(floor, FixedFallback(side)), rank, double.NaN, WarmedUp: false);
                return Percentile(_session.Percentile(_settings.Percentile), candidate, floor, rank);
            }

            // RollingPercentile is the default and the fallback for staged modes.
            case ThresholdMode.RollingPercentile:
            default:
            {
                if (_rolling.Count < _settings.MinSamples)
                    return new ThresholdResult(candidate >= Math.Max(floor, FixedFallback(side)), Math.Max(floor, FixedFallback(side)), rank, double.NaN, WarmedUp: false);
                return Percentile(_rolling.Percentile(_settings.Percentile), candidate, floor, rank);
            }
        }
    }

    private long FixedFallback(AggressorSide side) => _settings.FixedThresholdFor(side);

    /// <summary>
    /// Percentile rule: "big" means the candidate <b>strictly exceeds</b> the percentile
    /// value (so a stream of identical sizes flags nothing) and clears the absolute floor.
    /// The reported threshold is the smallest qualifying size.
    /// </summary>
    private static ThresholdResult Percentile(long percentileValue, long candidate, long floor, double rank)
    {
        long minQualifying = Math.Max(floor, percentileValue + 1);
        bool large = candidate >= floor && candidate > percentileValue;
        return new ThresholdResult(large, minQualifying, rank, double.NaN, WarmedUp: true);
    }
}
