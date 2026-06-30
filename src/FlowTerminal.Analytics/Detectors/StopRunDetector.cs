using FlowTerminal.Domain.Events;

namespace FlowTerminal.Analytics.Detectors;

public sealed record StopRunSettings
{
    public TimeSpan Lookback { get; init; } = TimeSpan.FromMinutes(5);
    public long BreakoutBufferTicks { get; init; } = 1;

    /// <summary>Short-window volume must exceed this multiple of the lookback's average burst.</summary>
    public double VolumeBurstFactor { get; init; } = 2.0;

    public TimeSpan ShortWindow { get; init; } = TimeSpan.FromSeconds(3);
    public TimeSpan Cooldown { get; init; } = TimeSpan.FromSeconds(10);
}

/// <summary>
/// Stop-run heuristic: price pushes through a recent swing high/low accompanied by a
/// burst of aggressive volume (the kind of move that runs resting stops). It reports
/// the observed breakout + volume burst; it does not assert intent, and continuation
/// vs rejection must be judged from subsequent price action.
/// </summary>
public sealed class StopRunDetector : IDetector
{
    private readonly StopRunSettings _settings;
    private readonly Queue<(DateTime Ts, long Price, long Qty)> _window = new();
    private DateTime _lastFire = DateTime.MinValue;

    public StopRunDetector(StopRunSettings? settings = null) => _settings = settings ?? new StopRunSettings();

    public string Name => "Stop-Run";
    public string Tooltip => "Price breaks a recent swing high/low with a burst of aggressive volume. Heuristic; continuation vs rejection needs follow-through.";
    public bool Enabled { get; set; } = true;

    public Detection? OnTrade(in MarketEvent e)
    {
        if (!Enabled || e.Type != MarketEventType.Trade || e.Quantity <= 0)
        {
            return null;
        }

        // Evict old, compute swing levels over prior window (excluding current print).
        Evict(e.ExchangeTimestampUtc);
        long swingHigh = long.MinValue, swingLow = long.MaxValue, totalVol = 0; int n = 0;
        long shortVol = 0; DateTime shortFrom = e.ExchangeTimestampUtc - _settings.ShortWindow;
        foreach (var (ts, price, qty) in _window)
        {
            swingHigh = Math.Max(swingHigh, price);
            swingLow = Math.Min(swingLow, price);
            totalVol += qty; n++;
            if (ts >= shortFrom) shortVol += qty;
        }

        // The current aggressive print is itself part of the burst.
        shortVol += e.Quantity;

        Detection? result = null;
        if (n >= 10)
        {
            double avgBurst = totalVol / Math.Max(1.0, _settings.Lookback.TotalSeconds / _settings.ShortWindow.TotalSeconds);
            bool burst = shortVol >= _settings.VolumeBurstFactor * avgBurst;
            bool cooled = (e.ExchangeTimestampUtc - _lastFire) >= _settings.Cooldown;

            if (burst && cooled)
            {
                if (e.PriceTicks >= swingHigh + _settings.BreakoutBufferTicks)
                {
                    _lastFire = e.ExchangeTimestampUtc;
                    result = Make(e, DetectionBias.Bullish, swingHigh, shortVol, avgBurst, "above swing high");
                }
                else if (e.PriceTicks <= swingLow - _settings.BreakoutBufferTicks)
                {
                    _lastFire = e.ExchangeTimestampUtc;
                    result = Make(e, DetectionBias.Bearish, swingLow, shortVol, avgBurst, "below swing low");
                }
            }
        }

        _window.Enqueue((e.ExchangeTimestampUtc, e.PriceTicks, e.Quantity));
        return result;
    }

    private Detection Make(in MarketEvent e, DetectionBias bias, long swing, long shortVol, double avgBurst, string desc) =>
        new(Name, e.ExchangeTimestampUtc, e.PriceTicks, bias, IsEstimated: false,
            $"Stop-run {desc} with volume burst",
            new Dictionary<string, double>
            {
                ["swing"] = swing,
                ["breakTicks"] = Math.Abs(e.PriceTicks - swing),
                ["shortVolume"] = shortVol,
                ["avgBurst"] = avgBurst,
            });

    private void Evict(DateTime now)
    {
        DateTime cutoff = now - _settings.Lookback;
        while (_window.Count > 0 && _window.Peek().Ts < cutoff)
        {
            _window.Dequeue();
        }
    }
}
