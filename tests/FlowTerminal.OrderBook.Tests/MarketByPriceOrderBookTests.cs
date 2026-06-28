using FlowTerminal.Domain.Events;
using FlowTerminal.Domain.Instruments;
using FlowTerminal.OrderBook;
using Xunit;

namespace FlowTerminal.OrderBook.Tests;

public class MarketByPriceOrderBookTests
{
    private static readonly DateTime Ts = new(2024, 6, 3, 14, 0, 0, DateTimeKind.Utc);

    private static MarketEvent Depth(Side side, long price, long size, long seq = 0)
    {
        var type = side == Side.Bid ? MarketEventType.BidUpdate : MarketEventType.AskUpdate;
        return MarketEvent.Quote(1, RootSymbol.NQ, "NQZ5", "CME Globex", type, Ts, Ts, side, price, size, exchangeSequence: seq);
    }

    private static MarketEvent Control(MarketEventType type, long seq = 0) =>
        MarketEvent.Control(1, RootSymbol.NQ, "NQZ5", "CME Globex", type, Ts, Ts, seq);

    private static MarketByPriceOrderBook BuiltBook()
    {
        var book = new MarketByPriceOrderBook();
        book.Apply(Control(MarketEventType.SnapshotStart));
        book.Apply(Depth(Side.Bid, 100, 10));
        book.Apply(Depth(Side.Bid, 99, 20));
        book.Apply(Depth(Side.Ask, 102, 15));
        book.Apply(Depth(Side.Ask, 103, 25));
        book.Apply(Control(MarketEventType.SnapshotEnd));
        return book;
    }

    [Fact]
    public void New_Book_Is_Invalid_Until_Snapshot_Completes()
    {
        var book = new MarketByPriceOrderBook();
        Assert.False(book.IsValid);
        Assert.NotNull(book.InvalidReason);

        book.Apply(Control(MarketEventType.SnapshotStart));
        book.Apply(Depth(Side.Bid, 100, 5));
        Assert.False(book.IsValid); // still inside snapshot
        book.Apply(Control(MarketEventType.SnapshotEnd));
        Assert.True(book.IsValid);
    }

    [Fact]
    public void Snapshot_Reconstructs_Best_Bid_And_Ask()
    {
        var book = BuiltBook();
        Assert.True(book.IsValid);
        Assert.Equal(100, book.BestBidTicks);
        Assert.Equal(102, book.BestAskTicks);
        Assert.Equal(10, book.SizeAt(Side.Bid, 100));
        Assert.Equal(25, book.SizeAt(Side.Ask, 103));
    }

    [Fact]
    public void Incremental_Update_Changes_Level_Size()
    {
        var book = BuiltBook();
        book.Apply(Depth(Side.Bid, 100, 50));
        Assert.Equal(50, book.SizeAt(Side.Bid, 100));
    }

    [Fact]
    public void Zero_Size_Removes_Level_And_Recomputes_Best()
    {
        var book = BuiltBook();
        book.Apply(Depth(Side.Bid, 100, 0)); // remove best bid
        Assert.Equal(0, book.SizeAt(Side.Bid, 100));
        Assert.Equal(99, book.BestBidTicks); // next level becomes best
    }

    [Fact]
    public void Cumulative_Depth_Aggregates_Levels()
    {
        var book = BuiltBook();
        Assert.Equal(30, book.CumulativeDepth(Side.Bid, 2));  // 10 + 20
        Assert.Equal(10, book.CumulativeDepth(Side.Bid, 1));
        Assert.Equal(40, book.CumulativeDepth(Side.Ask, 2));  // 15 + 25
    }

    [Fact]
    public void Crossed_Book_Is_Flagged_Invalid()
    {
        var book = BuiltBook();
        book.Apply(Depth(Side.Ask, 100, 5)); // ask at 100 <= best bid 100 → crossed
        Assert.False(book.IsValid);
        Assert.Equal("crossed book", book.InvalidReason);
    }

    [Fact]
    public void Negative_Size_Is_Rejected_And_Book_Marked_Invalid()
    {
        var book = BuiltBook();
        book.Apply(Depth(Side.Bid, 100, -5));
        Assert.True(book.SizeAt(Side.Bid, 100) >= 0);
        Assert.False(book.IsValid);
    }

    [Fact]
    public void Sequence_Gap_Invalidates_Until_Resnapshot()
    {
        var book = BuiltBook();
        book.Apply(Control(MarketEventType.SequenceGap, seq: 999));
        Assert.False(book.IsValid);
        Assert.Equal("sequence gap", book.InvalidReason);

        // Resynchronize from a fresh snapshot.
        book.Apply(Control(MarketEventType.SnapshotStart));
        book.Apply(Depth(Side.Bid, 101, 5));
        book.Apply(Depth(Side.Ask, 104, 5));
        book.Apply(Control(MarketEventType.SnapshotEnd));
        Assert.True(book.IsValid);
        Assert.Equal(101, book.BestBidTicks);
    }

    [Fact]
    public void Book_Clear_Empties_And_Invalidates()
    {
        var book = BuiltBook();
        book.Apply(Control(MarketEventType.BookClear));
        Assert.False(book.IsValid);
        Assert.Equal(0, book.SizeAt(Side.Bid, 100));
        Assert.Equal(BookSide.NoPrice, book.BestBidTicks);
    }

    [Fact]
    public void Checkpoint_RoundTrips_Book_State()
    {
        var book = BuiltBook();
        book.Apply(Depth(Side.Bid, 98, 7));
        var checkpoint = book.CreateCheckpoint();

        var restored = new MarketByPriceOrderBook();
        restored.RestoreCheckpoint(checkpoint);

        Assert.True(restored.IsValid);
        Assert.Equal(book.BestBidTicks, restored.BestBidTicks);
        Assert.Equal(book.BestAskTicks, restored.BestAskTicks);
        Assert.Equal(7, restored.SizeAt(Side.Bid, 98));
        Assert.Equal(book.CumulativeDepth(Side.Bid, 5), restored.CumulativeDepth(Side.Bid, 5));
    }

    [Fact]
    public void Final_State_Is_Deterministic_For_Same_Event_Sequence()
    {
        var a = BuiltBook();
        var b = BuiltBook();
        Assert.Equal(a.BestBidTicks, b.BestBidTicks);
        Assert.Equal(a.BestAskTicks, b.BestAskTicks);
        Assert.Equal(a.CumulativeDepth(Side.Bid, 10), b.CumulativeDepth(Side.Bid, 10));
        Assert.Equal(a.CumulativeDepth(Side.Ask, 10), b.CumulativeDepth(Side.Ask, 10));
    }
}
