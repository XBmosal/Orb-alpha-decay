namespace FlowTerminal.Analytics.Indicators;

/// <summary>
/// A fixed-capacity ring buffer of <see cref="double"/> values with O(1) running sum
/// and sum-of-squares, plus ordered indexing for weighted calculations. It is the
/// shared primitive behind the moving averages and oscillators: bounded memory, no
/// per-bar allocation, and no full-history recomputation.
///
/// Indexing is oldest-first: <c>this[0]</c> is the oldest retained value and
/// <c>this[Count-1]</c> is the most recent.
/// </summary>
public sealed class RollingWindow
{
    private readonly double[] _buffer;
    private int _head; // next write position
    private int _count;

    public RollingWindow(int capacity)
    {
        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Window capacity must be >= 1.");
        }

        _buffer = new double[capacity];
    }

    public int Capacity => _buffer.Length;
    public int Count => _count;
    public bool IsFull => _count == _buffer.Length;

    /// <summary>Running sum of the retained values.</summary>
    public double Sum { get; private set; }

    private double _sumSq;

    /// <summary>The oldest retained value (evicted on the next push at capacity), or NaN if empty.</summary>
    public double Oldest => _count == 0 ? double.NaN : this[0];

    public double this[int index]
    {
        get
        {
            if (index < 0 || index >= _count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            int start = (_head - _count + _buffer.Length) % _buffer.Length;
            return _buffer[(start + index) % _buffer.Length];
        }
    }

    /// <summary>Pushes a value, evicting the oldest when at capacity.</summary>
    public void Push(double value)
    {
        if (_count == _buffer.Length)
        {
            double old = _buffer[_head];
            Sum -= old;
            _sumSq -= old * old;
        }
        else
        {
            _count++;
        }

        _buffer[_head] = value;
        Sum += value;
        _sumSq += value * value;
        _head = (_head + 1) % _buffer.Length;
    }

    public void Clear()
    {
        _head = 0;
        _count = 0;
        Sum = 0;
        _sumSq = 0;
        Array.Clear(_buffer);
    }

    /// <summary>Arithmetic mean of the retained values, or NaN when empty.</summary>
    public double Mean => _count == 0 ? double.NaN : Sum / _count;

    /// <summary>Population variance (divisor N); clamped at 0 to absorb floating-point noise.</summary>
    public double PopulationVariance
    {
        get
        {
            if (_count == 0) return double.NaN;
            double mean = Sum / _count;
            return Math.Max(0.0, _sumSq / _count - mean * mean);
        }
    }

    /// <summary>Population standard deviation (divisor N) — the Bollinger Band convention.</summary>
    public double PopulationStdDev => Math.Sqrt(PopulationVariance);

    public double Min()
    {
        if (_count == 0) return double.NaN;
        double m = double.PositiveInfinity;
        for (int i = 0; i < _count; i++) m = Math.Min(m, this[i]);
        return m;
    }

    public double Max()
    {
        if (_count == 0) return double.NaN;
        double m = double.NegativeInfinity;
        for (int i = 0; i < _count; i++) m = Math.Max(m, this[i]);
        return m;
    }

    /// <summary>Mean absolute deviation about the current mean (used by CCI).</summary>
    public double MeanAbsoluteDeviation()
    {
        if (_count == 0) return double.NaN;
        double mean = Mean;
        double acc = 0;
        for (int i = 0; i < _count; i++) acc += Math.Abs(this[i] - mean);
        return acc / _count;
    }
}
