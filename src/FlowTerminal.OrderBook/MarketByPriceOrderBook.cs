using FlowTerminal.Domain.Events;

namespace FlowTerminal.OrderBook;

/// <summary>
/// Read-only market-by-price (aggregated depth) book. It reconstructs the book
/// from canonical events and is strictly observational — it has no notion of the
/// user's own orders.
///
/// Integrity model:
///   - A book is valid only after a completed snapshot (SnapshotStart…SnapshotEnd)
///     or once top-of-book/depth updates have established a non-crossed book.
///   - A <see cref="MarketEventType.SequenceGap"/> invalidates the book; it stays
///     invalid (and visibly so via <see cref="InvalidReason"/>) until a fresh
///     snapshot rebuilds it. Canonical events are never silently dropped — instead
///     the book enters a visible invalid state and resynchronizes.
///   - A crossed book (best bid ≥ best ask) is flagged invalid.
///   - Negative sizes are rejected so depth can never go negative.
///
/// Depth comes from BidUpdate/AskUpdate (aggregated size at a price). Trades do not
/// mutate displayed depth in MBP mode — the feed sends explicit depth updates.
/// </summary>
public sealed class MarketByPriceOrderBook : IOrderBook
{
    private readonly BookSide _bids = new(Side.Bid);
    private readonly BookSide _asks = new(Side.Ask);

    private bool _inSnapshot;
    private bool _establishedValid;
    private string? _invalidReason = "awaiting snapshot";
    private long _lastSequence;
    private DateTime _lastUpdateUtc;

    public bool IsValid => _invalidReason is null;

    public string? InvalidReason => _invalidReason;

    public long BestBidTicks => _bids.BestPrice;

    public long BestAskTicks => _asks.BestPrice;

    public int BidLevelCount => _bids.LevelCount;

    public int AskLevelCount => _asks.LevelCount;

    public long SizeAt(Side side, long priceTicks) => side switch
    {
        Side.Bid => _bids.SizeAt(priceTicks),
        Side.Ask => _asks.SizeAt(priceTicks),
        _ => 0,
    };

    public long CumulativeDepth(Side side, int levels) => side switch
    {
        Side.Bid => _bids.CumulativeSize(levels),
        Side.Ask => _asks.CumulativeSize(levels),
        _ => 0,
    };

    public bool Apply(in MarketEvent e)
    {
        _lastSequence = e.ExchangeSequence;
        _lastUpdateUtc = e.ExchangeTimestampUtc;

        switch (e.Type)
        {
            case MarketEventType.SnapshotStart:
                _bids.Clear();
                _asks.Clear();
                _inSnapshot = true;
                _establishedValid = false;
                _invalidReason = "rebuilding from snapshot";
                break;

            case MarketEventType.SnapshotEnd:
                _inSnapshot = false;
                _establishedValid = true;
                Revalidate();
                break;

            case MarketEventType.BookClear:
                _bids.Clear();
                _asks.Clear();
                _establishedValid = false;
                _invalidReason = "awaiting snapshot";
                break;

            case MarketEventType.SequenceGap:
                Invalidate("sequence gap");
                break;

            case MarketEventType.BidUpdate:
            case MarketEventType.Add:
            case MarketEventType.Modify:
            case MarketEventType.Cancel when e.Side == Side.Bid:
                if (!_bids.SetLevel(e.PriceTicks, NormalizedSize(e)))
                {
                    Invalidate("negative size rejected");
                    break;
                }

                EstablishOrRevalidate();
                break;

            case MarketEventType.AskUpdate:
            case MarketEventType.Cancel when e.Side == Side.Ask:
                if (!_asks.SetLevel(e.PriceTicks, NormalizedSize(e)))
                {
                    Invalidate("negative size rejected");
                    break;
                }

                EstablishOrRevalidate();
                break;

            // Trades / executions do not change displayed MBP depth.
            case MarketEventType.Trade:
            case MarketEventType.Execute:
            default:
                break;
        }

        return IsValid;
    }

    public void Clear()
    {
        _bids.Clear();
        _asks.Clear();
        _inSnapshot = false;
        _establishedValid = false;
        _invalidReason = "awaiting snapshot";
    }

    /// <summary>Captures the current book state as a checkpoint for replay seeking.</summary>
    public BookCheckpoint CreateCheckpoint()
    {
        var bids = new List<PriceLevel>(_bids.LevelCount);
        foreach (var kv in _bids.Levels())
        {
            bids.Add(new PriceLevel(kv.Key, kv.Value));
        }

        var asks = new List<PriceLevel>(_asks.LevelCount);
        foreach (var kv in _asks.Levels())
        {
            asks.Add(new PriceLevel(kv.Key, kv.Value));
        }

        return new BookCheckpoint(_lastSequence, _lastUpdateUtc, bids, asks, IsValid);
    }

    /// <summary>Restores book state from a checkpoint (used when seeking during replay).</summary>
    public void RestoreCheckpoint(BookCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        _bids.Clear();
        _asks.Clear();
        foreach (var level in checkpoint.Bids)
        {
            _bids.SetLevel(level.PriceTicks, level.Size);
        }

        foreach (var level in checkpoint.Asks)
        {
            _asks.SetLevel(level.PriceTicks, level.Size);
        }

        _lastSequence = checkpoint.ExchangeSequence;
        _lastUpdateUtc = checkpoint.AsOfUtc;
        _inSnapshot = false;
        _establishedValid = checkpoint.IsValid;
        Revalidate();
    }

    private static long NormalizedSize(in MarketEvent e) =>
        e.Type == MarketEventType.Cancel ? 0 : e.Quantity;

    private void EstablishOrRevalidate()
    {
        if (_inSnapshot)
        {
            return; // validity is decided at SnapshotEnd
        }

        // Incremental updates outside a snapshot establish a (top-of-book) book.
        _establishedValid = true;
        Revalidate();
    }

    private void Revalidate()
    {
        if (!_establishedValid)
        {
            _invalidReason = "awaiting snapshot";
            return;
        }

        long bid = _bids.BestPrice;
        long ask = _asks.BestPrice;
        if (bid != BookSide.NoPrice && ask != BookSide.NoPrice && bid >= ask)
        {
            _invalidReason = "crossed book";
            return;
        }

        _invalidReason = null;
    }

    private void Invalidate(string reason)
    {
        _establishedValid = false;
        _invalidReason = reason;
    }
}
