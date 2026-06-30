using FlowTerminal.Domain.Events;
using FlowTerminal.Domain.Instruments;

namespace FlowTerminal.Analytics.Detectors;

public sealed record LargeTradeSettings
{
    /// <summary>Absolute minimum size to ever consider "large".</summary>
    public long FixedThreshold { get; init; } = 50;

    /// <summary>When true, also require the trade to exceed a rolling percentile of recent sizes.</summary>
    public bool UseRollingPercentile { get; init; } = true;

    public double Percentile { get; init; } = 0.98;

    public int RollingWindow { get; init; } = 500;

    public static LargeTradeSettings ForNq() => new() { FixedThreshold = 50 };
    public static LargeTradeSettings ForEs() => new() { FixedThreshold = 75 };
}

/// <summary>
/// Flags outsized trades. A trade qualifies when it meets the fixed threshold and,
/// if enabled, also exceeds a rolling percentile of recent trade sizes (session-
/// adaptive). This marks unusually large prints; it does not assert who traded.
/// </summary>
public sealed class LargeTradeDetector : IDetector
{
    private readonly LargeTradeSettings _settings;
    private readonly Queue<long> _recent = new();
    private readonly long[] _sortBuffer;
    private long _sum;

    public LargeTradeDetector(LargeTradeSettings? settings = null)
    {
        _settings = settings ?? new LargeTradeSettings();
        _sortBuffer = new long[_settings.RollingWindow];
    }

    public string Name => "Large Trade";
    public string Tooltip => "Highlights unusually large prints (fixed + rolling-percentile threshold). Heuristic; not a claim about who traded.";
    public bool Enabled { get; set; } = true;

    public Detection? OnTrade(in MarketEvent e)
    {
        if (!Enabled || e.Type != MarketEventType.Trade || e.Quantity <= 0)
        {
            return null;
        }

        long size = e.Quantity;
        long threshold = _settings.FixedThreshold;
        bool percentileOk = true;

        if (_settings.UseRollingPercentile && _recent.Count >= 20)
        {
            long p = RollingPercentile(_settings.Percentile);
            threshold = Math.Max(threshold, p);
            percentileOk = size >= p;
        }

        Detection? result = null;
        if (size >= _settings.FixedThreshold && percentileOk)
        {
            result = new Detection(Name, e.ExchangeTimestampUtc, e.PriceTicks,
                e.Aggressor == AggressorSide.Buy ? DetectionBias.Bullish : DetectionBias.Bearish,
                IsEstimated: false,
                $"Large trade {size} (threshold {threshold})",
                new Dictionary<string, double> { ["size"] = size, ["threshold"] = threshold });
        }

        Push(size);
        return result;
    }

    private void Push(long size)
    {
        _recent.Enqueue(size);
        _sum += size;
        while (_recent.Count > _settings.RollingWindow)
        {
            _sum -= _recent.Dequeue();
        }
    }

    private long RollingPercentile(double p)
    {
        int n = _recent.Count;
        _recent.CopyTo(_sortBuffer, 0);
        Array.Sort(_sortBuffer, 0, n);
        int idx = (int)Math.Clamp(Math.Ceiling(p * n) - 1, 0, n - 1);
        return _sortBuffer[idx];
    }

    public static LargeTradeDetector For(RootSymbol root) =>
        new(root == RootSymbol.ES ? LargeTradeSettings.ForEs() : LargeTradeSettings.ForNq());
}
