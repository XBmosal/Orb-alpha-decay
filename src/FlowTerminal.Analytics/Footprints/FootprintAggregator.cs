namespace FlowTerminal.Analytics.Footprints;

/// <summary>One fully-derived footprint price level (immutable). Prices are integer ticks.</summary>
public readonly record struct FootprintPriceLevel(
    long PriceTicks,
    long BidVolume,
    long AskVolume,
    long UnknownVolume,
    int TradeCount,
    int BuyTradeCount,
    int SellTradeCount,
    long MaxTradeSize,
    long LargestBuyTrade,
    long LargestSellTrade,
    bool IsPoc,
    bool IsBuyImbalance,
    bool IsSellImbalance,
    bool IsStackedBuyImbalance,
    bool IsStackedSellImbalance,
    bool IsZeroPrint,
    bool IsUnfinishedAuction,
    int LargeTradeCount)
{
    public long TotalVolume => BidVolume + AskVolume + UnknownVolume;

    /// <summary>Aggressive buy − sell at this level.</summary>
    public long Delta => AskVolume - BidVolume;

    /// <summary>Delta as a percentage of bid+ask (0 when both are zero — never NaN/∞).</summary>
    public double DeltaPercent
    {
        get
        {
            long ba = BidVolume + AskVolume;
            return ba <= 0 ? 0.0 : 100.0 * Delta / ba;
        }
    }
}

/// <summary>A run of consecutive same-side diagonal imbalances (a stacked-imbalance zone).</summary>
public readonly record struct StackedZone(ImbalanceSide Side, long LowTicks, long HighTicks, int LevelCount);

/// <summary>
/// A complete, immutable footprint bar: OHLC, volumes, derived price levels and all
/// order-flow flags, plus a deterministic content hash for replay verification. Built
/// by <see cref="FootprintAggregator"/> from a <see cref="Footprint"/>; pure data, no
/// rendering. Levels are ordered ascending by price.
/// </summary>
public sealed record FootprintBar(
    long OpenTicks,
    long HighTicks,
    long LowTicks,
    long CloseTicks,
    DateTime StartUtc,
    DateTime EndUtc,
    bool IsClosed,
    long TotalVolume,
    long BuyVolume,
    long SellVolume,
    long UnknownVolume,
    long Delta,
    int TradeCount,
    long PocTicks,
    long MaxPositiveLevelDelta,
    long MaxNegativeLevelDelta,
    int BuyImbalanceCount,
    int SellImbalanceCount,
    int StackedImbalanceCount,
    AuctionState HighUnfinished,
    AuctionState LowUnfinished,
    IReadOnlyList<FootprintPriceLevel> Levels,
    IReadOnlyList<StackedZone> StackedZones,
    ulong Hash)
{
    public bool IsBullish => CloseTicks >= OpenTicks;

    public static FootprintBar Empty { get; } = new(
        0, 0, 0, 0, default, default, false, 0, 0, 0, 0, 0, 0, long.MinValue, 0, 0, 0, 0, 0,
        AuctionState.None, AuctionState.None, Array.Empty<FootprintPriceLevel>(), Array.Empty<StackedZone>(), 0);
}

/// <summary>
/// Turns an aggregated <see cref="Footprint"/> plus a bar's OHLC into a fully-derived
/// <see cref="FootprintBar"/> using <see cref="FootprintSettings"/>. Every calculation
/// is transparent and documented:
///
///   Delta(level)            = ask − bid
///   POC                     = max of the selected metric; ties → level closest to
///                             close, then higher (bullish) / lower (bearish), then higher price
///   Buy diagonal imbalance  = ask(P)  ≥ ratio · bid(P−1)   (zero denominator per policy)
///   Sell diagonal imbalance = bid(P)  ≥ ratio · ask(P+1)
///   Horizontal imbalance    = ask(P) vs bid(P) at the SAME price
///   Stacked imbalance       = ≥ N consecutive same-side imbalance levels (optional 1-gap)
///   Zero print              = one side 0 while the opposite ≥ min (optionally excl. extremes)
///   Unfinished auction      = bid ≥ min at the high / ask ≥ min at the low
///   Large trade             = a level's max single print ≥ threshold
///
/// Deterministic: identical trades + settings ⇒ identical bar and hash.
/// </summary>
public static class FootprintAggregator
{
    public static FootprintBar Build(
        Footprint fp, long openTicks, long highTicks, long lowTicks, long closeTicks,
        DateTime startUtc, DateTime endUtc, bool isClosed, FootprintSettings? settings = null)
    {
        var s = (settings ?? FootprintSettings.Default).Validate();
        var src = fp.Levels(); // ascending by price, buy/sell only

        int n = src.Count;
        var bid = new long[n];
        var ask = new long[n];
        var price = new long[n];
        for (int i = 0; i < n; i++)
        {
            price[i] = src[i].PriceTicks;
            ask[i] = src[i].BuyVolume;   // aggressive buys lift the ask
            bid[i] = src[i].SellVolume;  // aggressive sells hit the bid
        }

        long pocTicks = ComputePoc(fp, src, price, closeTicks, openTicks, s.PocSource);

        // Diagonal/horizontal imbalance flags per level.
        var buyImb = new bool[n];
        var sellImb = new bool[n];
        int buyImbCount = 0, sellImbCount = 0;
        for (int i = 0; i < n; i++)
        {
            long p = price[i];
            if (s.EnableDiagonalImbalance)
            {
                long bidBelow = fp.BidVolumeAt(p - 1);
                long askAbove = fp.AskVolumeAt(p + 1);
                buyImb[i] = Qualifies(ask[i], bidBelow, s.BuyImbalanceRatio, s);
                sellImb[i] = Qualifies(bid[i], askAbove, s.SellImbalanceRatio, s);
            }

            if (s.EnableHorizontalImbalance)
            {
                // Same-price comparison (distinct from diagonal); OR-ed into the flags.
                buyImb[i] |= Qualifies(ask[i], bid[i], s.BuyImbalanceRatio, s);
                sellImb[i] |= Qualifies(bid[i], ask[i], s.SellImbalanceRatio, s);
            }

            if (buyImb[i]) buyImbCount++;
            if (sellImb[i]) sellImbCount++;
        }

        var zones = DetectStacks(price, bid, ask, buyImb, sellImb, s);
        var stackedBuy = new bool[n];
        var stackedSell = new bool[n];
        foreach (var z in zones)
        {
            for (int i = 0; i < n; i++)
            {
                if (price[i] < z.LowTicks || price[i] > z.HighTicks) continue;
                if (z.Side == ImbalanceSide.Buy && buyImb[i]) stackedBuy[i] = true;
                if (z.Side == ImbalanceSide.Sell && sellImb[i]) stackedSell[i] = true;
            }
        }

        // Build the immutable level list with the remaining flags.
        var levels = new List<FootprintPriceLevel>(n);
        long maxPos = 0, maxNeg = 0;
        AuctionState highUnf = AuctionState.None, lowUnf = AuctionState.None;
        for (int i = 0; i < n; i++)
        {
            long p = price[i];
            long a = ask[i], b = bid[i];
            long unknown = fp.UnknownVolumeAt(p);
            long levelDelta = a - b;
            if (levelDelta > maxPos) maxPos = levelDelta;
            if (levelDelta < maxNeg) maxNeg = levelDelta;

            bool atHigh = p == highTicks, atLow = p == lowTicks;
            bool zero = s.ShowZeroPrints && IsZeroPrint(a, b, s, atHigh || atLow);
            bool unfinished = false;
            if (s.ShowUnfinishedAuctions)
            {
                if (atHigh && b >= s.UnfinishedMinVolume) { unfinished = true; highUnf = isClosed ? AuctionState.Confirmed : AuctionState.Developing; }
                if (atLow && a >= s.UnfinishedMinVolume) { unfinished = true; lowUnf = isClosed ? AuctionState.Confirmed : AuctionState.Developing; }
            }

            long maxTrade = fp.MaxTradeSizeAt(p);
            int largeCount = s.ShowLargeTrades && maxTrade >= s.LargeTradeThreshold ? 1 : 0;

            levels.Add(new FootprintPriceLevel(
                p, b, a, unknown,
                fp.TradeCountAt(p), fp.BuyTradeCountAt(p), fp.SellTradeCountAt(p),
                maxTrade, fp.LargestBuyTradeAt(p), fp.LargestSellTradeAt(p),
                IsPoc: p == pocTicks,
                IsBuyImbalance: buyImb[i],
                IsSellImbalance: sellImb[i],
                IsStackedBuyImbalance: stackedBuy[i],
                IsStackedSellImbalance: stackedSell[i],
                IsZeroPrint: zero,
                IsUnfinishedAuction: unfinished,
                LargeTradeCount: largeCount));
        }

        ulong hash = ComputeHash(openTicks, highTicks, lowTicks, closeTicks, levels, pocTicks, zones);

        return new FootprintBar(
            openTicks, highTicks, lowTicks, closeTicks, startUtc, endUtc, isClosed,
            fp.TotalVolume, fp.BuyVolume, fp.SellVolume, fp.UnknownVolume, fp.Delta, fp.TradeCount,
            pocTicks, maxPos, maxNeg, buyImbCount, sellImbCount, zones.Count, highUnf, lowUnf,
            levels, zones, hash);
    }

    private static long ComputePoc(Footprint fp, IReadOnlyList<Profiles.ProfileLevel> src,
        long[] price, long closeTicks, long openTicks, PocSource source)
    {
        if (src.Count == 0) return long.MinValue;

        long bestMetric = long.MinValue;
        long bestPrice = long.MinValue;
        bool bull = closeTicks >= openTicks;

        for (int i = 0; i < src.Count; i++)
        {
            long p = price[i];
            long metric = source switch
            {
                PocSource.BidVolume => src[i].SellVolume,
                PocSource.AskVolume => src[i].BuyVolume,
                PocSource.AbsoluteDelta => Math.Abs(src[i].BuyVolume - src[i].SellVolume),
                PocSource.TradeCount => fp.TradeCountAt(p),
                _ => src[i].TotalVolume,
            };

            if (metric > bestMetric)
            {
                bestMetric = metric;
                bestPrice = p;
            }
            else if (metric == bestMetric && bestPrice != long.MinValue)
            {
                // Deterministic tie rule: closest to close, then higher (bull)/lower (bear),
                // then higher price as the final fallback.
                long dCur = Math.Abs(p - closeTicks);
                long dBest = Math.Abs(bestPrice - closeTicks);
                if (dCur < dBest) bestPrice = p;
                else if (dCur == dBest)
                {
                    if (bull ? p > bestPrice : p < bestPrice) bestPrice = p;
                }
            }
        }

        return bestPrice;
    }

    private static bool Qualifies(long dominant, long compared, double ratio, FootprintSettings s)
    {
        if (dominant < s.MinImbalanceVolume || dominant <= 0)
        {
            return false;
        }

        if (compared <= 0)
        {
            return s.ZeroDenominator switch
            {
                ZeroDenominatorPolicy.Ignore => false,
                ZeroDenominatorPolicy.RequireMinNumerator => dominant >= s.MinImbalanceVolume,
                ZeroDenominatorPolicy.ExtremeImbalance => true,
                ZeroDenominatorPolicy.DenominatorFloor => dominant >= ratio * s.DenominatorFloor,
                _ => false,
            };
        }

        return dominant >= ratio * compared;
    }

    private static bool IsZeroPrint(long ask, long bid, FootprintSettings s, bool isExtreme)
    {
        if (s.ZeroPrintExcludeExtremes && isExtreme) return false;
        if (bid == 0 && ask >= s.ZeroPrintMinOpposite) return true;
        if (ask == 0 && bid >= s.ZeroPrintMinOpposite) return true;
        return false;
    }

    private static List<StackedZone> DetectStacks(
        long[] price, long[] bid, long[] ask, bool[] buyImb, bool[] sellImb, FootprintSettings s)
    {
        var zones = new List<StackedZone>();
        DetectSide(price, ask, buyImb, ImbalanceSide.Buy, s, zones);
        DetectSide(price, bid, sellImb, ImbalanceSide.Sell, s, zones);
        return zones;
    }

    private static void DetectSide(long[] price, long[] dominantVol, bool[] flag,
        ImbalanceSide side, FootprintSettings s, List<StackedZone> zones)
    {
        int n = price.Length;
        int i = 0;
        while (i < n)
        {
            if (!flag[i]) { i++; continue; }

            int start = i;
            int last = i;
            long vol = dominantVol[i];
            bool gapUsed = false;
            int j = i + 1;
            while (j < n)
            {
                long step = price[j] - price[last];
                if (flag[j] && step == 1)
                {
                    last = j; vol += dominantVol[j]; j++;
                }
                else if (flag[j] && step == 2 && s.AllowOneGap && !gapUsed)
                {
                    gapUsed = true; last = j; vol += dominantVol[j]; j++;
                }
                else
                {
                    break;
                }
            }

            int count = 0;
            for (int k = start; k <= last; k++) if (flag[k]) count++;
            if (count >= s.StackedCount && vol >= s.MinStackVolume)
            {
                zones.Add(new StackedZone(side, price[start], price[last], count));
            }

            i = Math.Max(last + 1, start + 1);
        }
    }

    private static ulong ComputeHash(
        long open, long high, long low, long close,
        List<FootprintPriceLevel> levels, long poc, List<StackedZone> zones)
    {
        // FNV-1a 64-bit over the bar's defining content — used to verify replay determinism.
        ulong h = 1469598103934665603UL;
        void Mix(long v)
        {
            unchecked
            {
                for (int b = 0; b < 8; b++) { h ^= (byte)(v >> (b * 8)); h *= 1099511628211UL; }
            }
        }

        Mix(open); Mix(high); Mix(low); Mix(close); Mix(poc);
        foreach (var l in levels)
        {
            Mix(l.PriceTicks); Mix(l.BidVolume); Mix(l.AskVolume); Mix(l.Delta);
            long flags = (l.IsPoc ? 1 : 0) | (l.IsBuyImbalance ? 2 : 0) | (l.IsSellImbalance ? 4 : 0)
                | (l.IsStackedBuyImbalance ? 8 : 0) | (l.IsStackedSellImbalance ? 16 : 0)
                | (l.IsZeroPrint ? 32 : 0) | (l.IsUnfinishedAuction ? 64 : 0) | (l.LargeTradeCount > 0 ? 128 : 0);
            Mix(flags);
        }

        foreach (var z in zones) { Mix((long)z.Side); Mix(z.LowTicks); Mix(z.HighTicks); Mix(z.LevelCount); }
        return h;
    }
}
