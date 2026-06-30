using FlowTerminal.Analytics.Bars;
using FlowTerminal.Analytics.Delta;
using FlowTerminal.Analytics.Profiles;
using FlowTerminal.Analytics.Vwap;
using FlowTerminal.Domain.Events;
using FlowTerminal.MarketData.Abstractions;
using FlowTerminal.OrderBook;

namespace FlowTerminal.Replay;

public sealed record ReplayStateHash(
    long EventCount,
    long TradeCount,
    long BestBidTicks,
    long BestAskTicks,
    long Cvd,
    long PocTicks,
    long FinalBarClose,
    int BarCount,
    long VwapTicksScaled)
{
    /// <summary>A single 64-bit digest combining all state fields (FNV-1a).</summary>
    public ulong Digest()
    {
        ulong h = 1469598103934665603UL;
        void Mix(long v)
        {
            unchecked
            {
                h ^= (ulong)v;
                h *= 1099511628211UL;
            }
        }

        Mix(EventCount); Mix(TradeCount); Mix(BestBidTicks); Mix(BestAskTicks);
        Mix(Cvd); Mix(PocTicks); Mix(FinalBarClose); Mix(BarCount); Mix(VwapTicksScaled);
        return h;
    }
}

public sealed record ReplayValidationReport(bool Match, ReplayStateHash First, ReplayStateHash Second)
{
    public string Describe() => Match
        ? $"DETERMINISTIC ✓  digest={First.Digest():X16}  events={First.EventCount} bars={First.BarCount}"
        : $"MISMATCH ✗  first={First.Digest():X16} second={Second.Digest():X16}\n  first={First}\n  second={Second}";
}

/// <summary>
/// Replays a recorded session through the SAME book and analytics engines as live,
/// twice, and compares a hash of the final reconstructed state. Identical hashes
/// prove the replay (and therefore the whole analytics pipeline) is deterministic.
/// </summary>
public sealed class ReplayValidator
{
    private readonly IReplaySource _source;
    private readonly ReplayRequest _request;

    public ReplayValidator(IReplaySource source, ReplayRequest request)
    {
        _source = source;
        _request = request;
    }

    public async Task<ReplayValidationReport> ValidateAsync(CancellationToken cancellationToken = default)
    {
        var first = await ReplayOnceAsync(cancellationToken).ConfigureAwait(false);
        var second = await ReplayOnceAsync(cancellationToken).ConfigureAwait(false);
        return new ReplayValidationReport(first.Digest() == second.Digest(), first, second);
    }

    public async Task<ReplayStateHash> ReplayOnceAsync(CancellationToken cancellationToken = default)
    {
        var book = new MarketByPriceOrderBook();
        var cvd = new CvdCalculator();
        var profile = new VolumeProfile();
        var vwap = new VwapCalculator();
        var bars = BarAggregator.Time(TimeSpan.FromMinutes(1));

        long eventCount = 0, tradeCount = 0;
        long finalBarClose = 0;
        int barCount = 0;

        await foreach (var e in _source.ReadAsync(_request, cancellationToken).ConfigureAwait(false))
        {
            eventCount++;
            book.Apply(e);
            if (e.Type == MarketEventType.Trade)
            {
                tradeCount++;
                cvd.Add(e);
                profile.AddTrade(e);
                vwap.Add(e);
                if (bars.AddTrade(e) is { } completed)
                {
                    barCount++;
                    finalBarClose = completed.CloseTicks;
                }
            }
        }

        if (bars.HasDeveloping)
        {
            finalBarClose = bars.Developing.CloseTicks;
        }

        var vwapValue = vwap.Value();
        long vwapScaled = double.IsNaN(vwapValue.VwapTicks) ? 0 : (long)Math.Round(vwapValue.VwapTicks * 1000);

        return new ReplayStateHash(
            eventCount, tradeCount,
            book.BestBidTicks, book.BestAskTicks,
            cvd.CumulativeDelta, profile.PocTicks(),
            finalBarClose, barCount, vwapScaled);
    }
}
