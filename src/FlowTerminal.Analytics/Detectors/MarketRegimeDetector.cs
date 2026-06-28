using FlowTerminal.Domain.Events;

namespace FlowTerminal.Analytics.Detectors;

public enum MarketRegime
{
    Quiet,
    Normal,
    Fast,
    Extreme,
}

public sealed record MarketRegimeSettings
{
    public TimeSpan Window { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Upper trades/sec bounds for Quiet and Normal and Fast (above Fast = Extreme).</summary>
    public double QuietMaxTps { get; init; } = 2;
    public double NormalMaxTps { get; init; } = 10;
    public double FastMaxTps { get; init; } = 30;
}

/// <summary>
/// Classifies the current market regime (Quiet / Normal / Fast / Extreme) from a
/// rolling window of trade rate, book-update rate, realized volatility, and spread.
/// A descriptive summary of activity; not predictive.
/// </summary>
public sealed class MarketRegimeDetector : IDetector
{
    private readonly MarketRegimeSettings _settings;
    private readonly Queue<(DateTime Ts, long Price, bool IsTrade)> _window = new();
    private long _bestBid, _bestAsk;

    public MarketRegimeDetector(MarketRegimeSettings? settings = null) => _settings = settings ?? new MarketRegimeSettings();

    public string Name => "Market Regime";
    public string Tooltip => "Quiet/Normal/Fast/Extreme from rolling trade rate, book-update rate, realized volatility and spread. Descriptive, not predictive.";
    public bool Enabled { get; set; } = true;

    public MarketRegime Current { get; private set; } = MarketRegime.Quiet;

    /// <summary>Feeds an event; returns a detection only when the regime changes.</summary>
    public Detection? OnEvent(in MarketEvent e)
    {
        if (!Enabled) return null;

        switch (e.Type)
        {
            case MarketEventType.BidUpdate: _bestBid = e.PriceTicks; break;
            case MarketEventType.AskUpdate: _bestAsk = e.PriceTicks; break;
        }

        if (e.Type is MarketEventType.Trade or MarketEventType.BidUpdate or MarketEventType.AskUpdate)
        {
            _window.Enqueue((e.ExchangeTimestampUtc, e.PriceTicks, e.Type == MarketEventType.Trade));
        }

        DateTime cutoff = e.ExchangeTimestampUtc - _settings.Window;
        while (_window.Count > 0 && _window.Peek().Ts < cutoff) _window.Dequeue();

        double secs = _settings.Window.TotalSeconds;
        int trades = 0; var prices = new List<long>();
        foreach (var (_, price, isTrade) in _window)
        {
            if (isTrade) { trades++; prices.Add(price); }
        }

        double tps = trades / secs;
        var regime = tps <= _settings.QuietMaxTps ? MarketRegime.Quiet
            : tps <= _settings.NormalMaxTps ? MarketRegime.Normal
            : tps <= _settings.FastMaxTps ? MarketRegime.Fast
            : MarketRegime.Extreme;

        if (regime == Current) return null;

        Current = regime;
        double vol = RealizedVol(prices);
        long spread = _bestAsk > 0 && _bestBid > 0 ? _bestAsk - _bestBid : 0;
        return new Detection(Name, e.ExchangeTimestampUtc, e.PriceTicks, DetectionBias.None,
            IsEstimated: false,
            $"Regime → {regime} ({tps:F1} trades/s)",
            new Dictionary<string, double>
            {
                ["tradesPerSecond"] = tps,
                ["bookUpdatesPerSecond"] = (_window.Count - trades) / secs,
                ["realizedVolTicks"] = vol,
                ["spreadTicks"] = spread,
            });
    }

    private static double RealizedVol(List<long> prices)
    {
        if (prices.Count < 2) return 0;
        double sumSq = 0; int n = 0;
        for (int i = 1; i < prices.Count; i++) { long d = prices[i] - prices[i - 1]; sumSq += d * d; n++; }
        return Math.Sqrt(sumSq / n);
    }
}
