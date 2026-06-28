using FlowTerminal.Domain.Events;

namespace FlowTerminal.Charting.Tape;

public readonly record struct TapeRow(
    DateTime ExchangeTimestampUtc,
    DateTime ReceiveTimestampUtc,
    long PriceTicks,
    long Quantity,
    AggressorSide Aggressor,
    bool IsLarge);

public sealed record TapeFilter
{
    public long MinSize { get; init; } = 0;
    public long MaxSize { get; init; } = long.MaxValue;
    public bool BuyOnly { get; init; }
    public bool SellOnly { get; init; }

    public bool Accepts(in MarketEvent t)
    {
        if (t.Quantity < MinSize || t.Quantity > MaxSize)
        {
            return false;
        }

        if (BuyOnly && t.Aggressor != AggressorSide.Buy)
        {
            return false;
        }

        if (SellOnly && t.Aggressor != AggressorSide.Sell)
        {
            return false;
        }

        return true;
    }
}

/// <summary>
/// Virtualized Time and Sales model backed by a fixed-capacity ring buffer, so
/// memory stays bounded regardless of session length. UI batches reads of the
/// latest rows; all canonical trades are still processed (filtering only affects
/// what is displayed, never what is recorded). Large trades are flagged for
/// highlighting. There are no order-entry controls here — it is a read-only tape.
/// </summary>
public sealed class TimeAndSalesModel
{
    private readonly TapeRow[] _ring;
    private int _head;          // next write position
    private int _count;
    private long _runningVolume;

    public TimeAndSalesModel(int capacity = 4096, long largeTradeThreshold = 50)
    {
        if (capacity < 16)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _ring = new TapeRow[capacity];
        LargeTradeThreshold = largeTradeThreshold;
    }

    public long LargeTradeThreshold { get; set; }
    public TapeFilter Filter { get; set; } = new();
    public int Count => _count;
    public long RunningVolume => _runningVolume;

    /// <summary>Processes a trade. Returns true if it passed the display filter and was buffered.</summary>
    public bool Add(in MarketEvent trade)
    {
        if (trade.Type != MarketEventType.Trade || trade.Quantity <= 0)
        {
            return false;
        }

        _runningVolume += trade.Quantity;

        if (!Filter.Accepts(trade))
        {
            return false;
        }

        var row = new TapeRow(
            trade.ExchangeTimestampUtc, trade.ReceiveTimestampUtc,
            trade.PriceTicks, trade.Quantity, trade.Aggressor,
            trade.Quantity >= LargeTradeThreshold);

        _ring[_head] = row;
        _head = (_head + 1) % _ring.Length;
        if (_count < _ring.Length)
        {
            _count++;
        }

        return true;
    }

    /// <summary>Most recent rows, newest first, up to <paramref name="max"/>.</summary>
    public IReadOnlyList<TapeRow> Latest(int max)
    {
        int n = Math.Min(max, _count);
        var result = new List<TapeRow>(n);
        for (int i = 0; i < n; i++)
        {
            int idx = (_head - 1 - i + _ring.Length) % _ring.Length;
            result.Add(_ring[idx]);
        }

        return result;
    }

    /// <summary>Clears the visible buffer without affecting recorded data.</summary>
    public void ClearVisible()
    {
        _head = 0;
        _count = 0;
    }
}
