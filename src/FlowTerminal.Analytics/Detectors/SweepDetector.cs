using FlowTerminal.Domain.Events;

namespace FlowTerminal.Analytics.Detectors;

public sealed record SweepSettings
{
    /// <summary>Minimum number of distinct consecutive price levels taken.</summary>
    public int MinLevels { get; init; } = 3;

    /// <summary>Maximum time between consecutive prints to stay in the same sweep.</summary>
    public TimeSpan MaxGap { get; init; } = TimeSpan.FromMilliseconds(750);

    /// <summary>Minimum total volume across the sweep.</summary>
    public long MinVolume { get; init; } = 30;
}

/// <summary>
/// Detects a sweep: a same-direction run of aggressive trades that takes several
/// consecutive price levels within a short time, consuming liquidity quickly. It
/// describes observed tape behaviour; it makes no claim about the actor.
/// </summary>
public sealed class SweepDetector : IDetector
{
    private readonly SweepSettings _settings;

    private DetectionBias _runBias = DetectionBias.None;
    private long _lastPrice;
    private DateTime _lastTime;
    private int _levels;
    private long _volume;
    private long _startPrice;
    private DateTime _startTime;

    public SweepDetector(SweepSettings? settings = null) => _settings = settings ?? new SweepSettings();

    public string Name => "Sweep";
    public string Tooltip => "Aggressive same-direction run across consecutive price levels in a short window. Heuristic.";
    public bool Enabled { get; set; } = true;

    public Detection? OnTrade(in MarketEvent e)
    {
        if (!Enabled || e.Type != MarketEventType.Trade || e.Quantity <= 0 || e.Aggressor == AggressorSide.Unknown)
        {
            return null;
        }

        var bias = e.Aggressor == AggressorSide.Buy ? DetectionBias.Bullish : DetectionBias.Bearish;
        bool continues =
            _runBias == bias &&
            (e.ExchangeTimestampUtc - _lastTime) <= _settings.MaxGap &&
            ((bias == DetectionBias.Bullish && e.PriceTicks >= _lastPrice) ||
             (bias == DetectionBias.Bearish && e.PriceTicks <= _lastPrice));

        if (!continues)
        {
            // Start a fresh run.
            _runBias = bias;
            _startPrice = e.PriceTicks;
            _startTime = e.ExchangeTimestampUtc;
            _levels = 1;
            _volume = e.Quantity;
            _lastPrice = e.PriceTicks;
            _lastTime = e.ExchangeTimestampUtc;
            return null;
        }

        if (e.PriceTicks != _lastPrice)
        {
            _levels++;
        }

        _volume += e.Quantity;
        _lastPrice = e.PriceTicks;
        _lastTime = e.ExchangeTimestampUtc;

        if (_levels >= _settings.MinLevels && _volume >= _settings.MinVolume)
        {
            long levelsSpanned = Math.Abs(e.PriceTicks - _startPrice) + 1;
            var detection = new Detection(Name, e.ExchangeTimestampUtc, e.PriceTicks, bias,
                IsEstimated: false,
                $"Sweep {(bias == DetectionBias.Bullish ? "up" : "down")} {_levels} levels, vol {_volume}",
                new Dictionary<string, double>
                {
                    ["levels"] = _levels,
                    ["volume"] = _volume,
                    ["ticksSpanned"] = levelsSpanned,
                    ["durationMs"] = (e.ExchangeTimestampUtc - _startTime).TotalMilliseconds,
                });
            // Reset so each completed sweep reports once; further prints start a new run.
            _runBias = DetectionBias.None;
            return detection;
        }

        return null;
    }
}
