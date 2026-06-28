using FlowTerminal.Domain.Events;

namespace FlowTerminal.Analytics.Bars;

/// <summary>
/// Consumes canonical Trade events and emits completed bars. One developing bar is
/// maintained at a time. All aggregators are deterministic and depend only on the
/// trade stream, so live and replay produce identical bars.
/// </summary>
public interface IBarAggregator
{
    BarKind Kind { get; }

    bool HasDeveloping { get; }

    Bar Developing { get; }

    /// <summary>Adds a trade; returns the bar that just completed, if any.</summary>
    Bar? AddTrade(in MarketEvent trade);

    /// <summary>Force-closes the developing bar (e.g. at session end); returns it if present.</summary>
    Bar? CloseCurrent();
}

internal abstract class BarAggregatorBase : IBarAggregator
{
    protected readonly BarBuilder Builder;

    protected BarAggregatorBase(BarKind kind) => Builder = new BarBuilder(kind);

    public BarKind Kind => Builder.Kind;
    public bool HasDeveloping => Builder.IsOpen;
    public Bar Developing => Builder.Snapshot();

    public abstract Bar? AddTrade(in MarketEvent trade);

    public Bar? CloseCurrent()
    {
        if (!Builder.IsOpen)
        {
            return null;
        }

        var bar = Builder.Snapshot();
        Builder.Reset();
        return bar;
    }

    protected static bool IsTrade(in MarketEvent e) => e.Type == MarketEventType.Trade && e.Quantity > 0;
}

/// <summary>
/// Time bars aligned to fixed UTC interval boundaries (bucket =
/// floor(ticks / interval) * interval). A trade in a later bucket closes the
/// developing bar before opening the new one. Empty buckets are not emitted.
/// </summary>
internal sealed class TimeBarAggregator : BarAggregatorBase
{
    private readonly long _intervalTicks;
    private DateTime _bucketStart;

    public TimeBarAggregator(TimeSpan interval) : base(BarKind.Time)
    {
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval));
        }

        _intervalTicks = interval.Ticks;
    }

    public override Bar? AddTrade(in MarketEvent trade)
    {
        if (!IsTrade(trade))
        {
            return null;
        }

        var bucket = BucketStart(trade.ExchangeTimestampUtc);
        Bar? completed = null;

        if (Builder.IsOpen && bucket > _bucketStart)
        {
            completed = Builder.Snapshot(_bucketStart.AddTicks(_intervalTicks));
            Builder.Reset();
        }

        if (!Builder.IsOpen)
        {
            _bucketStart = bucket;
            Builder.Begin(bucket, trade.PriceTicks);
        }

        Builder.Add(trade);
        return completed;
    }

    private DateTime BucketStart(DateTime utc)
    {
        long aligned = utc.Ticks / _intervalTicks * _intervalTicks;
        return new DateTime(aligned, DateTimeKind.Utc);
    }
}

/// <summary>Tick bars: a new bar every N trades.</summary>
internal sealed class TickBarAggregator : BarAggregatorBase
{
    private readonly int _ticksPerBar;

    public TickBarAggregator(int tradesPerBar) : base(BarKind.Tick)
    {
        if (tradesPerBar < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(tradesPerBar));
        }

        _ticksPerBar = tradesPerBar;
    }

    public override Bar? AddTrade(in MarketEvent trade)
    {
        if (!IsTrade(trade))
        {
            return null;
        }

        Builder.Add(trade);
        if (Builder.TradeCount >= _ticksPerBar)
        {
            var bar = Builder.Snapshot();
            Builder.Reset();
            return bar;
        }

        return null;
    }
}

/// <summary>Volume bars: the bar closes once accumulated volume reaches the threshold.</summary>
internal sealed class VolumeBarAggregator : BarAggregatorBase
{
    private readonly long _volumePerBar;

    public VolumeBarAggregator(long volumePerBar) : base(BarKind.Volume)
    {
        if (volumePerBar < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(volumePerBar));
        }

        _volumePerBar = volumePerBar;
    }

    public override Bar? AddTrade(in MarketEvent trade)
    {
        if (!IsTrade(trade))
        {
            return null;
        }

        Builder.Add(trade);
        if (Builder.Volume >= _volumePerBar)
        {
            var bar = Builder.Snapshot();
            Builder.Reset();
            return bar;
        }

        return null;
    }
}

/// <summary>
/// Range bars: the bar closes once its high−low span reaches the configured tick
/// range. The trade that breaches the range is included in the closing bar.
/// </summary>
internal sealed class RangeBarAggregator : BarAggregatorBase
{
    private readonly long _rangeTicks;

    public RangeBarAggregator(long rangeTicks) : base(BarKind.Range)
    {
        if (rangeTicks < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(rangeTicks));
        }

        _rangeTicks = rangeTicks;
    }

    public override Bar? AddTrade(in MarketEvent trade)
    {
        if (!IsTrade(trade))
        {
            return null;
        }

        Builder.Add(trade);
        if (Builder.SpanTicks >= _rangeTicks)
        {
            var bar = Builder.Snapshot();
            Builder.Reset();
            return bar;
        }

        return null;
    }
}

/// <summary>Factory for the supported bar aggregators.</summary>
public static class BarAggregator
{
    public static IBarAggregator Time(TimeSpan interval) => new TimeBarAggregator(interval);

    public static IBarAggregator Tick(int tradesPerBar) => new TickBarAggregator(tradesPerBar);

    public static IBarAggregator Volume(long volumePerBar) => new VolumeBarAggregator(volumePerBar);

    public static IBarAggregator Range(long rangeTicks) => new RangeBarAggregator(rangeTicks);
}
