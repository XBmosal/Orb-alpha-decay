using FlowTerminal.Analytics.Bars;

namespace FlowTerminal.Analytics.Detectors;

public sealed record DeltaDivergenceSettings
{
    /// <summary>How many completed bars to look back for the comparison swing.</summary>
    public int Lookback { get; init; } = 10;
}

/// <summary>
/// Delta-divergence heuristic on completed bars:
///   - Bearish: price makes a higher high than the lookback swing, but bar delta is
///     weaker (lower) than at that swing.
///   - Bullish: price makes a lower low, but bar delta is stronger (higher).
/// Configurable lookback. This is descriptive, with no predictive guarantee.
/// </summary>
public sealed class DeltaDivergenceDetector : IDetector
{
    private readonly DeltaDivergenceSettings _settings;
    private readonly List<Bar> _bars = new();

    public DeltaDivergenceDetector(DeltaDivergenceSettings? settings = null) => _settings = settings ?? new DeltaDivergenceSettings();

    public string Name => "Delta Divergence";
    public string Tooltip => "Price makes a new high/low but bar delta does not confirm. Heuristic; no predictive guarantee.";
    public bool Enabled { get; set; } = true;

    /// <summary>Feeds a completed bar; returns a detection when divergence is found.</summary>
    public Detection? OnBar(in Bar bar)
    {
        if (!Enabled) return null;
        _bars.Add(bar);
        int window = _settings.Lookback;
        if (_bars.Count <= window) return null;

        // Compare the just-closed bar to the swing within the prior `window` bars.
        int from = _bars.Count - 1 - window;
        int swingHighIdx = from, swingLowIdx = from;
        for (int i = from; i < _bars.Count - 1; i++)
        {
            if (_bars[i].HighTicks > _bars[swingHighIdx].HighTicks) swingHighIdx = i;
            if (_bars[i].LowTicks < _bars[swingLowIdx].LowTicks) swingLowIdx = i;
        }

        var cur = _bars[^1];
        var hi = _bars[swingHighIdx];
        var lo = _bars[swingLowIdx];

        if (cur.HighTicks > hi.HighTicks && cur.Delta < hi.Delta)
        {
            return new Detection(Name, cur.EndUtc, cur.HighTicks, DetectionBias.Bearish, IsEstimated: false,
                "Higher high with weaker delta",
                new Dictionary<string, double> { ["curDelta"] = cur.Delta, ["swingDelta"] = hi.Delta });
        }

        if (cur.LowTicks < lo.LowTicks && cur.Delta > lo.Delta)
        {
            return new Detection(Name, cur.EndUtc, cur.LowTicks, DetectionBias.Bullish, IsEstimated: false,
                "Lower low with stronger delta",
                new Dictionary<string, double> { ["curDelta"] = cur.Delta, ["swingDelta"] = lo.Delta });
        }

        return null;
    }
}
