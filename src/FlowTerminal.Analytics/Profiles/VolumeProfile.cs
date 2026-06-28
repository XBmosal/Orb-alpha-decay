using FlowTerminal.Domain.Events;

namespace FlowTerminal.Analytics.Profiles;

public readonly record struct ProfileLevel(long PriceTicks, long BuyVolume, long SellVolume)
{
    public long TotalVolume => BuyVolume + SellVolume;
    public long Delta => BuyVolume - SellVolume;
}

public readonly record struct ValueArea(long PocTicks, long ValTicks, long VahTicks);

/// <summary>
/// Volume-at-price profile built incrementally from trades. Buy volume is volume
/// where the aggressor lifted the offer (traded at ask); sell volume is volume
/// where the aggressor hit the bid. POC, value area high/low are derived on demand,
/// so "developing" POC/VA is simply a query at the current state.
///
/// All math is integer-tick based and safe against empty input.
/// </summary>
public sealed class VolumeProfile
{
    private readonly Dictionary<long, long> _buy = new();
    private readonly Dictionary<long, long> _sell = new();

    public long TotalVolume { get; private set; }
    public long TotalBuyVolume { get; private set; }
    public long TotalSellVolume { get; private set; }

    public void AddTrade(in MarketEvent trade)
    {
        if (trade.Type != MarketEventType.Trade || trade.Quantity <= 0)
        {
            return;
        }

        AddAt(trade.PriceTicks, trade.Quantity, trade.Aggressor);
    }

    public void AddAt(long priceTicks, long quantity, AggressorSide aggressor)
    {
        if (quantity <= 0)
        {
            return;
        }

        TotalVolume += quantity;
        switch (aggressor)
        {
            case AggressorSide.Buy:
                _buy[priceTicks] = _buy.GetValueOrDefault(priceTicks) + quantity;
                TotalBuyVolume += quantity;
                break;
            case AggressorSide.Sell:
                _sell[priceTicks] = _sell.GetValueOrDefault(priceTicks) + quantity;
                TotalSellVolume += quantity;
                break;
            default:
                // Unknown aggressor: count toward total volume only (split evenly is
                // avoided to keep delta honest). Store as buy/sell-neutral.
                _buy.TryAdd(priceTicks, _buy.GetValueOrDefault(priceTicks));
                break;
        }
    }

    /// <summary>Aggressive-buy (traded-at-ask) volume at a price.</summary>
    public long BuyVolumeAt(long priceTicks) => _buy.GetValueOrDefault(priceTicks);

    /// <summary>Aggressive-sell (traded-at-bid) volume at a price.</summary>
    public long SellVolumeAt(long priceTicks) => _sell.GetValueOrDefault(priceTicks);

    public long VolumeAt(long priceTicks) =>
        _buy.GetValueOrDefault(priceTicks) + _sell.GetValueOrDefault(priceTicks);

    public long DeltaAt(long priceTicks) =>
        _buy.GetValueOrDefault(priceTicks) - _sell.GetValueOrDefault(priceTicks);

    /// <summary>All levels sorted ascending by price.</summary>
    public IReadOnlyList<ProfileLevel> Levels()
    {
        var prices = new SortedSet<long>(_buy.Keys);
        prices.UnionWith(_sell.Keys);
        var list = new List<ProfileLevel>(prices.Count);
        foreach (var p in prices)
        {
            list.Add(new ProfileLevel(p, _buy.GetValueOrDefault(p), _sell.GetValueOrDefault(p)));
        }

        return list;
    }

    /// <summary>Point of control: the price with the greatest total volume (ties → higher price).</summary>
    public long PocTicks()
    {
        long bestPrice = long.MinValue;
        long bestVol = -1;
        foreach (var level in Levels())
        {
            if (level.TotalVolume > bestVol || (level.TotalVolume == bestVol && level.PriceTicks > bestPrice))
            {
                bestVol = level.TotalVolume;
                bestPrice = level.PriceTicks;
            }
        }

        return bestPrice;
    }

    /// <summary>
    /// Value area covering <paramref name="percent"/> of volume (default 0.70),
    /// expanded outward from the POC by repeatedly taking the higher-volume
    /// neighbouring level. Returns POC/VAL/VAH in ticks.
    /// </summary>
    public ValueArea ComputeValueArea(double percent = 0.70)
    {
        var levels = Levels();
        if (levels.Count == 0)
        {
            return new ValueArea(long.MinValue, long.MinValue, long.MinValue);
        }

        long poc = PocTicks();
        int pocIndex = 0;
        for (int i = 0; i < levels.Count; i++)
        {
            if (levels[i].PriceTicks == poc)
            {
                pocIndex = i;
                break;
            }
        }

        long target = (long)Math.Ceiling(TotalVolume * percent);
        long accumulated = levels[pocIndex].TotalVolume;
        int lo = pocIndex;
        int hi = pocIndex;

        while (accumulated < target && (lo > 0 || hi < levels.Count - 1))
        {
            long below = lo > 0 ? levels[lo - 1].TotalVolume : -1;
            long above = hi < levels.Count - 1 ? levels[hi + 1].TotalVolume : -1;

            if (above >= below)
            {
                hi++;
                accumulated += levels[hi].TotalVolume;
            }
            else
            {
                lo--;
                accumulated += levels[lo].TotalVolume;
            }
        }

        return new ValueArea(poc, levels[lo].PriceTicks, levels[hi].PriceTicks);
    }

    public void Reset()
    {
        _buy.Clear();
        _sell.Clear();
        TotalVolume = TotalBuyVolume = TotalSellVolume = 0;
    }
}
