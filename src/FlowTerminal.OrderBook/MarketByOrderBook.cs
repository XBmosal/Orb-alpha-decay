using FlowTerminal.Domain.Events;

namespace FlowTerminal.OrderBook;

/// <summary>
/// Read-only market-by-order book (available only when the feed is MBO-entitled).
/// It tracks individual resting orders by id within FIFO price-level queues, which
/// enables queue analysis and replenishment tracking. It is observational only:
/// there is no concept of the user's own orders or their queue position.
///
/// Operations mirror the canonical event set: Add, Modify (size change at the same
/// price), Cancel, Execute (size reduction), and price-changing Modify which is
/// treated as a Replace (remove + re-add at the tail of the new level, losing
/// priority — as exchanges do). Unknown order ids and duplicate adds are counted
/// and flagged rather than corrupting the book.
/// </summary>
public sealed class MarketByOrderBook : IOrderBook
{
    private sealed class Order
    {
        public long Id;
        public Side Side;
        public long PriceTicks;
        public long Size;
    }

    private sealed class Level
    {
        public long AggregateSize;
        public readonly LinkedList<Order> Queue = new(); // head = highest priority (FIFO)
    }

    private readonly Dictionary<long, Order> _orders = new();
    private readonly SortedDictionary<long, Level> _bidLevels = new(Comparer<long>.Create((a, b) => b.CompareTo(a)));
    private readonly SortedDictionary<long, Level> _askLevels = new();

    private string? _invalidReason;
    private long _unknownOrderCount;
    private long _duplicateAddCount;

    public bool IsValid => _invalidReason is null;

    public string? InvalidReason => _invalidReason;

    public long UnknownOrderCount => _unknownOrderCount;

    public long DuplicateAddCount => _duplicateAddCount;

    public long BestBidTicks => FirstKey(_bidLevels);

    public long BestAskTicks => FirstKey(_askLevels);

    public long SizeAt(Side side, long priceTicks)
    {
        var levels = LevelsFor(side);
        return levels.TryGetValue(priceTicks, out var level) ? level.AggregateSize : 0;
    }

    public long CumulativeDepth(Side side, int levels)
    {
        long total = 0;
        int i = 0;
        foreach (var kv in LevelsFor(side))
        {
            if (i++ >= levels)
            {
                break;
            }

            total += kv.Value.AggregateSize;
        }

        return total;
    }

    /// <summary>Orders resting at a price level, in FIFO priority order (head first).</summary>
    public IReadOnlyList<(long OrderId, long Size)> QueueAt(Side side, long priceTicks)
    {
        if (!LevelsFor(side).TryGetValue(priceTicks, out var level))
        {
            return Array.Empty<(long, long)>();
        }

        var result = new List<(long, long)>(level.Queue.Count);
        foreach (var order in level.Queue)
        {
            result.Add((order.Id, order.Size));
        }

        return result;
    }

    public bool Apply(in MarketEvent e)
    {
        switch (e.Type)
        {
            case MarketEventType.SnapshotStart:
            case MarketEventType.BookClear:
                Clear();
                if (e.Type == MarketEventType.SnapshotStart)
                {
                    _invalidReason = "rebuilding from snapshot";
                }

                break;

            case MarketEventType.SnapshotEnd:
                _invalidReason = null;
                Revalidate();
                break;

            case MarketEventType.SequenceGap:
                _invalidReason = "sequence gap";
                break;

            case MarketEventType.Add:
                AddOrder(e);
                break;

            case MarketEventType.Modify:
                ModifyOrder(e);
                break;

            case MarketEventType.Cancel:
                CancelOrder(e);
                break;

            case MarketEventType.Execute:
                ExecuteOrder(e);
                break;

            default:
                break;
        }

        return IsValid;
    }

    public void Clear()
    {
        _orders.Clear();
        _bidLevels.Clear();
        _askLevels.Clear();
        _invalidReason = null;
        Revalidate();
    }

    private void AddOrder(in MarketEvent e)
    {
        if (e.Quantity <= 0)
        {
            return; // ignore non-positive add
        }

        if (_orders.ContainsKey(e.OrderId))
        {
            _duplicateAddCount++;
            return; // duplicate add is ignored, not corrupting
        }

        var order = new Order { Id = e.OrderId, Side = e.Side, PriceTicks = e.PriceTicks, Size = e.Quantity };
        _orders[order.Id] = order;
        var level = GetOrCreateLevel(order.Side, order.PriceTicks);
        level.Queue.AddLast(order);
        level.AggregateSize += order.Size;
        Revalidate();
    }

    private void ModifyOrder(in MarketEvent e)
    {
        if (!_orders.TryGetValue(e.OrderId, out var order))
        {
            _unknownOrderCount++;
            return;
        }

        if (e.PriceTicks != order.PriceTicks)
        {
            // Price change = replace: remove and re-add at tail (priority reset).
            RemoveOrder(order);
            if (e.Quantity > 0)
            {
                var moved = new Order { Id = e.OrderId, Side = e.Side, PriceTicks = e.PriceTicks, Size = e.Quantity };
                _orders[moved.Id] = moved;
                var level = GetOrCreateLevel(moved.Side, moved.PriceTicks);
                level.Queue.AddLast(moved);
                level.AggregateSize += moved.Size;
            }

            Revalidate();
            return;
        }

        // Same price: adjust size in place (priority preserved).
        var lvl = LevelsFor(order.Side)[order.PriceTicks];
        lvl.AggregateSize += e.Quantity - order.Size;
        order.Size = e.Quantity;
        if (order.Size <= 0)
        {
            RemoveOrder(order);
        }

        Revalidate();
    }

    private void CancelOrder(in MarketEvent e)
    {
        if (!_orders.TryGetValue(e.OrderId, out var order))
        {
            _unknownOrderCount++;
            return;
        }

        RemoveOrder(order);
        Revalidate();
    }

    private void ExecuteOrder(in MarketEvent e)
    {
        if (!_orders.TryGetValue(e.OrderId, out var order))
        {
            _unknownOrderCount++;
            return;
        }

        long reduceBy = e.Quantity > 0 ? e.Quantity : order.Size;
        long applied = Math.Min(reduceBy, order.Size);
        order.Size -= applied;
        var level = LevelsFor(order.Side)[order.PriceTicks];
        level.AggregateSize -= applied;
        if (order.Size <= 0)
        {
            RemoveOrder(order);
        }

        Revalidate();
    }

    private void RemoveOrder(Order order)
    {
        if (!_orders.Remove(order.Id))
        {
            return;
        }

        var levels = LevelsFor(order.Side);
        if (levels.TryGetValue(order.PriceTicks, out var level))
        {
            level.Queue.Remove(order);
            level.AggregateSize -= order.Size;
            if (level.Queue.Count == 0 || level.AggregateSize <= 0)
            {
                levels.Remove(order.PriceTicks);
            }
        }
    }

    private Level GetOrCreateLevel(Side side, long priceTicks)
    {
        var levels = LevelsFor(side);
        if (!levels.TryGetValue(priceTicks, out var level))
        {
            level = new Level();
            levels[priceTicks] = level;
        }

        return level;
    }

    private SortedDictionary<long, Level> LevelsFor(Side side) => side == Side.Bid ? _bidLevels : _askLevels;

    private static long FirstKey(SortedDictionary<long, Level> levels)
    {
        foreach (var key in levels.Keys)
        {
            return key;
        }

        return BookSide.NoPrice;
    }

    private void Revalidate()
    {
        long bid = BestBidTicks;
        long ask = BestAskTicks;
        if (_invalidReason is "sequence gap" or "rebuilding from snapshot")
        {
            return; // sticky until a snapshot resolves it
        }

        _invalidReason = (bid != BookSide.NoPrice && ask != BookSide.NoPrice && bid >= ask)
            ? "crossed book"
            : null;
    }
}
