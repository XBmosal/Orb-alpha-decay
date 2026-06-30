namespace FlowTerminal.Analytics.Detectors;

/// <summary>Directional bias of a detection. Buy/aggressive-up = Bullish; sell/down = Bearish.</summary>
public enum DetectionBias
{
    None,
    Bullish,
    Bearish,
}

/// <summary>
/// A single detector signal. Detectors NEVER claim guaranteed institutional intent;
/// <see cref="IsEstimated"/> flags results inferred without direct exchange evidence
/// (e.g. iceberg from trades+MBP), and <see cref="Measurements"/> carries the
/// supporting numbers so the user can judge for themselves.
/// </summary>
public sealed record Detection(
    string DetectorName,
    DateTime TimestampUtc,
    long PriceTicks,
    DetectionBias Bias,
    bool IsEstimated,
    string Description,
    IReadOnlyDictionary<string, double> Measurements)
{
    public string Label => IsEstimated ? $"{DetectorName} (Estimated)" : DetectorName;
}

/// <summary>Common surface for every detector: a name, a tooltip, and an on/off toggle.</summary>
public interface IDetector
{
    string Name { get; }

    /// <summary>Plain-language explanation shown as a tooltip. States that it is heuristic.</summary>
    string Tooltip { get; }

    bool Enabled { get; set; }
}
