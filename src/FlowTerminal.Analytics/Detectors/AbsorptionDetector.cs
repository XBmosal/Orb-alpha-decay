using FlowTerminal.Domain.Events;

namespace FlowTerminal.Analytics.Detectors;

public sealed record AbsorptionSettings
{
    /// <summary>Aggressive volume that must be absorbed within the window.</summary>
    public long MinVolume { get; init; } = 200;

    /// <summary>Maximum price movement (ticks) allowed while volume is absorbed.</summary>
    public long MaxMoveTicks { get; init; } = 2;

    public TimeSpan Window { get; init; } = TimeSpan.FromSeconds(5);
}

/// <summary>
/// Detects absorption: a large amount of aggressive volume executes within a tight
/// price band over a short window without price moving away — i.e. resting
/// liquidity absorbs the aggression. Observational; not a claim about intent.
/// </summary>
public sealed class AbsorptionDetector : IDetector
{
    private readonly AbsorptionSettings _settings;

    private DateTime _windowStart;
    private long _anchorPrice;
    private long _minPrice, _maxPrice;
    private long _buyVol, _sellVol;
    private bool _active;
    private bool _fired;

    public AbsorptionDetector(AbsorptionSettings? settings = null) => _settings = settings ?? new AbsorptionSettings();

    public string Name => "Absorption";
    public string Tooltip => "Heavy aggressive volume absorbed within a tight price band over a short window. Heuristic.";
    public bool Enabled { get; set; } = true;

    public Detection? OnTrade(in MarketEvent e)
    {
        if (!Enabled || e.Type != MarketEventType.Trade || e.Quantity <= 0)
        {
            return null;
        }

        bool windowExpired = _active && (e.ExchangeTimestampUtc - _windowStart) > _settings.Window;
        bool movedTooFar = _active && (Math.Max(_maxPrice, e.PriceTicks) - Math.Min(_minPrice, e.PriceTicks)) > _settings.MaxMoveTicks;

        if (!_active || windowExpired || movedTooFar)
        {
            _active = true;
            _fired = false;
            _windowStart = e.ExchangeTimestampUtc;
            _anchorPrice = e.PriceTicks;
            _minPrice = _maxPrice = e.PriceTicks;
            _buyVol = _sellVol = 0;
        }

        _minPrice = Math.Min(_minPrice, e.PriceTicks);
        _maxPrice = Math.Max(_maxPrice, e.PriceTicks);
        if (e.Aggressor == AggressorSide.Buy) _buyVol += e.Quantity;
        else if (e.Aggressor == AggressorSide.Sell) _sellVol += e.Quantity;

        long total = _buyVol + _sellVol;
        if (!_fired && total >= _settings.MinVolume && (_maxPrice - _minPrice) <= _settings.MaxMoveTicks)
        {
            _fired = true;
            // Absorption side = the resting side that held: opposite the dominant aggressor.
            var bias = _buyVol >= _sellVol ? DetectionBias.Bearish : DetectionBias.Bullish;
            return new Detection(Name, e.ExchangeTimestampUtc, _anchorPrice, bias,
                IsEstimated: false,
                $"Absorbed {total} within {_maxPrice - _minPrice} ticks",
                new Dictionary<string, double>
                {
                    ["totalVolume"] = total,
                    ["buyVolume"] = _buyVol,
                    ["sellVolume"] = _sellVol,
                    ["moveTicks"] = _maxPrice - _minPrice,
                });
        }

        return null;
    }
}
