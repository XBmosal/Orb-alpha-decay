using FlowTerminal.Domain.Events;

namespace FlowTerminal.Analytics.Detectors;

public sealed record IcebergSettings
{
    /// <summary>Executed/displayed ratio above which refilling looks iceberg-like.</summary>
    public double RefillRatio { get; init; } = 4.0;

    /// <summary>Minimum executed volume at a price before considering it.</summary>
    public long MinExecuted { get; init; } = 100;
}

/// <summary>
/// Iceberg heuristic. With only trades + market-by-price it can never be certain, so
/// every result is flagged <see cref="Detection.IsEstimated"/> = true with a
/// confidence measurement. It looks for a price where executed volume greatly
/// exceeds the largest displayed size — consistent with a hidden refilling order.
/// (When market-by-order is entitled, a stronger, non-estimated detector is used.)
/// </summary>
public sealed class IcebergDetector : IDetector
{
    private readonly IcebergSettings _settings;
    private readonly Dictionary<long, long> _maxDisplayed = new();
    private readonly Dictionary<long, long> _executed = new();
    private readonly HashSet<long> _fired = new();

    public IcebergDetector(IcebergSettings? settings = null) => _settings = settings ?? new IcebergSettings();

    public string Name => "Iceberg";
    public string Tooltip => "Estimated: executed volume at a price far exceeds the largest displayed size (possible hidden refill). Never certain without MBO.";
    public bool Enabled { get; set; } = true;

    public Detection? OnEvent(in MarketEvent e)
    {
        if (!Enabled) return null;

        if (e.Type is MarketEventType.BidUpdate or MarketEventType.AskUpdate)
        {
            long cur = _maxDisplayed.GetValueOrDefault(e.PriceTicks);
            if (e.Quantity > cur) _maxDisplayed[e.PriceTicks] = e.Quantity;
            return null;
        }

        if (e.Type != MarketEventType.Trade || e.Quantity <= 0) return null;

        long exec = _executed.GetValueOrDefault(e.PriceTicks) + e.Quantity;
        _executed[e.PriceTicks] = exec;
        long displayed = _maxDisplayed.GetValueOrDefault(e.PriceTicks);

        if (displayed > 0 && exec >= _settings.MinExecuted && exec >= _settings.RefillRatio * displayed && _fired.Add(e.PriceTicks))
        {
            double confidence = Math.Min(1.0, exec / (double)(displayed * _settings.RefillRatio));
            return new Detection(Name, e.ExchangeTimestampUtc, e.PriceTicks,
                e.Aggressor == AggressorSide.Buy ? DetectionBias.Bearish : DetectionBias.Bullish, // refill rests opposite the aggressor
                IsEstimated: true,
                $"Possible iceberg: executed {exec} vs displayed {displayed}",
                new Dictionary<string, double>
                {
                    ["executed"] = exec,
                    ["maxDisplayed"] = displayed,
                    ["ratio"] = displayed > 0 ? exec / (double)displayed : 0,
                    ["confidence"] = confidence,
                });
        }

        return null;
    }
}

public sealed record ReplenishmentSettings
{
    public int MinReplenishments { get; init; } = 3;
    public long MinSize { get; init; } = 20;
    public TimeSpan Window { get; init; } = TimeSpan.FromSeconds(10);
}

/// <summary>
/// Liquidity-replenishment detector: a price level that is repeatedly depleted and
/// restored within a time window. Per-price counter; observational.
/// </summary>
public sealed class ReplenishmentDetector : IDetector
{
    private sealed class State
    {
        public long LastSize;
        public bool Depleted;
        public int Count;
        public DateTime FirstUtc;
        public bool Initialized;
    }

    private readonly ReplenishmentSettings _settings;
    private readonly Dictionary<long, State> _bid = new();
    private readonly Dictionary<long, State> _ask = new();

    public ReplenishmentDetector(ReplenishmentSettings? settings = null) => _settings = settings ?? new ReplenishmentSettings();

    public string Name => "Replenishment";
    public string Tooltip => "A price level repeatedly depleted and restored within a short window. Heuristic.";
    public bool Enabled { get; set; } = true;

    public Detection? OnDepth(in MarketEvent e)
    {
        if (!Enabled) return null;
        Dictionary<long, State> map;
        if (e.Type == MarketEventType.BidUpdate) map = _bid;
        else if (e.Type == MarketEventType.AskUpdate) map = _ask;
        else return null;

        if (!map.TryGetValue(e.PriceTicks, out var s))
        {
            s = new State { FirstUtc = e.ExchangeTimestampUtc };
            map[e.PriceTicks] = s;
        }

        long size = Math.Max(0, e.Quantity);
        if (!s.Initialized) { s.Initialized = true; s.LastSize = size; s.Depleted = size == 0; return null; }

        if (e.ExchangeTimestampUtc - s.FirstUtc > _settings.Window)
        {
            s.Count = 0; s.FirstUtc = e.ExchangeTimestampUtc;
        }

        if (size == 0) s.Depleted = true;
        else if (s.Depleted && size >= _settings.MinSize)
        {
            s.Depleted = false;
            s.Count++;
            if (s.Count >= _settings.MinReplenishments)
            {
                s.Count = 0;
                s.LastSize = size;
                return new Detection(Name, e.ExchangeTimestampUtc, e.PriceTicks,
                    e.Type == MarketEventType.BidUpdate ? DetectionBias.Bullish : DetectionBias.Bearish,
                    IsEstimated: false,
                    $"Level replenished {_settings.MinReplenishments}× within window",
                    new Dictionary<string, double> { ["size"] = size });
            }
        }

        s.LastSize = size;
        return null;
    }
}
