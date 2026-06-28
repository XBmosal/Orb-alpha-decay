using FlowTerminal.Analytics.Profiles;
using FlowTerminal.Domain.Events;

namespace FlowTerminal.Analytics.Footprints;

public enum ImbalanceSide
{
    Buy,
    Sell,
}

public readonly record struct ImbalanceMarker(long PriceTicks, ImbalanceSide Side, long Volume, long ComparedVolume);

/// <summary>
/// Configuration for imbalance detection. Buy and sell thresholds are independent.
/// </summary>
public sealed record ImbalanceSettings
{
    /// <summary>Required ratio of dominant to compared volume (e.g. 3.0 = 300%).</summary>
    public double BuyRatio { get; init; } = 3.0;

    public double SellRatio { get; init; } = 3.0;

    /// <summary>Minimum dominant-side volume for a level to qualify.</summary>
    public long MinVolume { get; init; } = 1;

    public static ImbalanceSettings Default { get; } = new();
}

/// <summary>
/// A per-bar footprint: bid/ask volume at each price plus derived statistics and
/// imbalance detection. Prices are integer ticks, so "one tick below/above" is
/// simply price−1 / price+1.
///
/// Diagonal imbalance (per the documented definition):
///   - Buy imbalance at price P: ask volume at P vs bid volume at P−1.
///   - Sell imbalance at price P: bid volume at P vs ask volume at P+1.
/// Zero denominators are handled safely (a non-zero dominant side over zero
/// compared volume qualifies when it meets MinVolume) — never NaN/Infinity.
///
/// Mapping: aggressive buys trade at the ask (ask volume); aggressive sells trade
/// at the bid (bid volume).
/// </summary>
public sealed class Footprint
{
    private readonly VolumeProfile _profile = new();

    public long TotalVolume => _profile.TotalVolume;
    public long BuyVolume => _profile.TotalBuyVolume;   // traded at ask
    public long SellVolume => _profile.TotalSellVolume; // traded at bid
    public long Delta => BuyVolume - SellVolume;

    public void AddTrade(in MarketEvent trade) => _profile.AddTrade(trade);

    public void AddAt(long priceTicks, long quantity, AggressorSide aggressor) =>
        _profile.AddAt(priceTicks, quantity, aggressor);

    public long AskVolumeAt(long priceTicks) => _profile.BuyVolumeAt(priceTicks);

    public long BidVolumeAt(long priceTicks) => _profile.SellVolumeAt(priceTicks);

    public long TotalVolumeAt(long priceTicks) => _profile.VolumeAt(priceTicks);

    public long DeltaAt(long priceTicks) => _profile.DeltaAt(priceTicks);

    public long PocTicks() => _profile.PocTicks();

    public ValueArea ValueArea(double percent = 0.70) => _profile.ComputeValueArea(percent);

    public IReadOnlyList<ProfileLevel> Levels() => _profile.Levels();

    /// <summary>Detects diagonal imbalances using the documented buy/sell definitions.</summary>
    public IReadOnlyList<ImbalanceMarker> DiagonalImbalances(ImbalanceSettings? settings = null)
    {
        settings ??= ImbalanceSettings.Default;
        var markers = new List<ImbalanceMarker>();

        foreach (var level in _profile.Levels())
        {
            long p = level.PriceTicks;

            // Buy imbalance: ask volume at P vs bid volume at P-1.
            long ask = _profile.BuyVolumeAt(p);
            long bidBelow = _profile.SellVolumeAt(p - 1);
            if (Qualifies(ask, bidBelow, settings.BuyRatio, settings.MinVolume))
            {
                markers.Add(new ImbalanceMarker(p, ImbalanceSide.Buy, ask, bidBelow));
            }

            // Sell imbalance: bid volume at P vs ask volume at P+1.
            long bid = _profile.SellVolumeAt(p);
            long askAbove = _profile.BuyVolumeAt(p + 1);
            if (Qualifies(bid, askAbove, settings.SellRatio, settings.MinVolume))
            {
                markers.Add(new ImbalanceMarker(p, ImbalanceSide.Sell, bid, askAbove));
            }
        }

        return markers;
    }

    /// <summary>
    /// Horizontal imbalance at a price: bid vs ask volume at the SAME price level.
    /// Buy when ask dominates bid; sell when bid dominates ask.
    /// </summary>
    public IReadOnlyList<ImbalanceMarker> HorizontalImbalances(ImbalanceSettings? settings = null)
    {
        settings ??= ImbalanceSettings.Default;
        var markers = new List<ImbalanceMarker>();

        foreach (var level in _profile.Levels())
        {
            long ask = level.BuyVolume;
            long bid = level.SellVolume;

            if (Qualifies(ask, bid, settings.BuyRatio, settings.MinVolume))
            {
                markers.Add(new ImbalanceMarker(level.PriceTicks, ImbalanceSide.Buy, ask, bid));
            }
            else if (Qualifies(bid, ask, settings.SellRatio, settings.MinVolume))
            {
                markers.Add(new ImbalanceMarker(level.PriceTicks, ImbalanceSide.Sell, bid, ask));
            }
        }

        return markers;
    }

    public void Reset() => _profile.Reset();

    private static bool Qualifies(long dominant, long compared, double ratio, long minVolume)
    {
        if (dominant < minVolume)
        {
            return false;
        }

        if (compared <= 0)
        {
            return true; // dominant volume over no opposing volume; safe, no division
        }

        return dominant >= ratio * compared;
    }
}
