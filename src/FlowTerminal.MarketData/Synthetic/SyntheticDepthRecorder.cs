namespace FlowTerminal.MarketData.Synthetic;

/// <summary>A point-in-time realism snapshot of the synthetic book, for diagnostics.</summary>
public readonly record struct SyntheticDiagnostics(
    SyntheticRegime Regime,
    int BidLevels,
    int AskLevels,
    long BestBidTicks,
    long BestAskTicks,
    long SpreadTicks,
    long MedianLevelSize,
    long MaxLevelSize,
    int WallCount,
    long TotalExecuted,
    long TradeCount,
    long EventCount);

/// <summary>
/// Periodically samples the live synthetic book and tallies running totals so the
/// debug area can show that the generated market is genuinely heavy-tailed, regime
/// driven and book-derived (it does not invent any visual liquidity). Sampling is
/// observational only — it never mutates the book or the event stream.
/// </summary>
public sealed class SyntheticDepthRecorder
{
    private readonly int _sampleEverySteps;
    private SyntheticDiagnostics _last;

    public SyntheticDepthRecorder(int sampleEverySteps = 64) =>
        _sampleEverySteps = Math.Max(1, sampleEverySteps);

    public long TotalExecuted { get; private set; }
    public long TradeCount { get; private set; }
    public long EventCount { get; private set; }

    public void NoteExecution(long qty)
    {
        TotalExecuted += qty;
        TradeCount++;
    }

    public void NoteEvent() => EventCount++;

    public SyntheticDiagnostics Latest => _last;

    public void MaybeSample(int step, SyntheticRegime regime, SyntheticOrderBook book)
    {
        if (step % _sampleEverySteps != 0)
        {
            return;
        }

        var sizes = new List<long>(book.BidLevelCount + book.AskLevelCount);
        int walls = 0;
        long max = 0;
        foreach (var lvl in book.Bids.Values) { sizes.Add(lvl.Displayed); if (lvl.IsWall) walls++; if (lvl.Displayed > max) max = lvl.Displayed; }
        foreach (var lvl in book.Asks.Values) { sizes.Add(lvl.Displayed); if (lvl.IsWall) walls++; if (lvl.Displayed > max) max = lvl.Displayed; }

        long median = 0;
        if (sizes.Count > 0)
        {
            sizes.Sort();
            median = sizes[sizes.Count / 2];
        }

        long spread = book.BestBidTicks != SyntheticOrderBook.NoPrice && book.BestAskTicks != SyntheticOrderBook.NoPrice
            ? book.BestAskTicks - book.BestBidTicks
            : 0;

        _last = new SyntheticDiagnostics(
            regime, book.BidLevelCount, book.AskLevelCount,
            book.BestBidTicks, book.BestAskTicks, spread,
            median, max, walls, TotalExecuted, TradeCount, EventCount);
    }
}
