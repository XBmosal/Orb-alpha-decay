using FlowTerminal.Domain.Events;
using FlowTerminal.Domain.Instruments;
using FlowTerminal.OrderBook;
using Xunit;

namespace FlowTerminal.OrderBook.Tests;

public class MarketByOrderBookTests
{
    private static readonly DateTime Ts = new(2024, 6, 3, 14, 0, 0, DateTimeKind.Utc);

    private static MarketEvent Order(MarketEventType type, long orderId, Side side, long price, long qty)
        => new(1, RootSymbol.NQ, "NQZ5", "CME Globex", type, Ts, Ts,
               side: side, priceTicks: price, quantity: qty, orderId: orderId);

    private static MarketByOrderBook SeededBook()
    {
        var book = new MarketByOrderBook();
        book.Apply(Order(MarketEventType.Add, 1, Side.Bid, 100, 5));
        book.Apply(Order(MarketEventType.Add, 2, Side.Bid, 100, 7)); // same level, behind #1
        book.Apply(Order(MarketEventType.Add, 3, Side.Ask, 102, 4));
        return book;
    }

    [Fact]
    public void Add_Aggregates_Level_And_Sets_Best()
    {
        var book = SeededBook();
        Assert.Equal(12, book.SizeAt(Side.Bid, 100));
        Assert.Equal(100, book.BestBidTicks);
        Assert.Equal(102, book.BestAskTicks);
    }

    [Fact]
    public void Queue_Preserves_Fifo_Priority()
    {
        var book = SeededBook();
        var queue = book.QueueAt(Side.Bid, 100);
        Assert.Equal(2, queue.Count);
        Assert.Equal(1, queue[0].OrderId); // first added has priority
        Assert.Equal(2, queue[1].OrderId);
    }

    [Fact]
    public void Partial_Execution_Reduces_Order_And_Level()
    {
        var book = SeededBook();
        book.Apply(Order(MarketEventType.Execute, 1, Side.Bid, 100, 2));
        Assert.Equal(10, book.SizeAt(Side.Bid, 100)); // 12 - 2
        Assert.Equal(3, book.QueueAt(Side.Bid, 100)[0].Size); // order #1: 5 - 2
    }

    [Fact]
    public void Full_Execution_Removes_Order_Keeps_Priority_Of_Rest()
    {
        var book = SeededBook();
        book.Apply(Order(MarketEventType.Execute, 1, Side.Bid, 100, 5));
        var queue = book.QueueAt(Side.Bid, 100);
        Assert.Single(queue);
        Assert.Equal(2, queue[0].OrderId);
        Assert.Equal(7, book.SizeAt(Side.Bid, 100));
    }

    [Fact]
    public void Partial_Cancel_Via_Modify_Reduces_Size_In_Place()
    {
        var book = SeededBook();
        book.Apply(Order(MarketEventType.Modify, 2, Side.Bid, 100, 3)); // 7 → 3
        Assert.Equal(8, book.SizeAt(Side.Bid, 100)); // 5 + 3
        var queue = book.QueueAt(Side.Bid, 100);
        Assert.Equal(2, queue[1].OrderId); // priority preserved on same-price modify
    }

    [Fact]
    public void Full_Cancel_Removes_Order()
    {
        var book = SeededBook();
        book.Apply(Order(MarketEventType.Cancel, 1, Side.Bid, 100, 0));
        Assert.Equal(7, book.SizeAt(Side.Bid, 100));
        Assert.Single(book.QueueAt(Side.Bid, 100));
    }

    [Fact]
    public void Replace_Moves_Price_And_Resets_Priority()
    {
        var book = SeededBook();
        // Modify order #1 to a new price → replace (loses priority at new level).
        book.Apply(Order(MarketEventType.Add, 4, Side.Bid, 99, 9));
        book.Apply(Order(MarketEventType.Modify, 1, Side.Bid, 99, 5));
        Assert.Equal(0, book.SizeAt(Side.Bid, 100) - 7); // #2 (7) remains at 100
        Assert.Equal(7, book.SizeAt(Side.Bid, 100));
        var q99 = book.QueueAt(Side.Bid, 99);
        Assert.Equal(4, q99[0].OrderId);  // #4 keeps priority
        Assert.Equal(1, q99[1].OrderId);  // #1 re-added at tail
    }

    [Fact]
    public void Duplicate_Add_Is_Counted_Not_Applied()
    {
        var book = SeededBook();
        book.Apply(Order(MarketEventType.Add, 1, Side.Bid, 100, 99)); // id 1 already exists
        Assert.Equal(1, book.DuplicateAddCount);
        Assert.Equal(12, book.SizeAt(Side.Bid, 100)); // unchanged
    }

    [Fact]
    public void Unknown_Order_Id_Is_Counted_For_Cancel_Modify_Execute()
    {
        var book = SeededBook();
        book.Apply(Order(MarketEventType.Cancel, 999, Side.Bid, 100, 0));
        book.Apply(Order(MarketEventType.Modify, 998, Side.Bid, 100, 1));
        book.Apply(Order(MarketEventType.Execute, 997, Side.Bid, 100, 1));
        Assert.Equal(3, book.UnknownOrderCount);
        Assert.Equal(12, book.SizeAt(Side.Bid, 100)); // unchanged
    }

    [Fact]
    public void Sequence_Gap_Invalidates_Mbo_Book()
    {
        var book = SeededBook();
        Assert.True(book.IsValid);
        book.Apply(MarketEvent.Control(1, RootSymbol.NQ, "NQZ5", "CME Globex", MarketEventType.SequenceGap, Ts, Ts));
        Assert.False(book.IsValid);
        Assert.Equal("sequence gap", book.InvalidReason);
    }

    [Fact]
    public void Empty_Level_Is_Removed_When_All_Orders_Leave()
    {
        var book = SeededBook();
        book.Apply(Order(MarketEventType.Cancel, 3, Side.Ask, 102, 0));
        Assert.Equal(0, book.SizeAt(Side.Ask, 102));
        Assert.Equal(BookSide.NoPrice, book.BestAskTicks);
    }
}
