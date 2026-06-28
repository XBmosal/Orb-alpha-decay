using FlowTerminal.Analytics.Profiles;
using FlowTerminal.Charting.Dom;
using FlowTerminal.Charting.Tape;
using FlowTerminal.Charting.Workspaces;
using FlowTerminal.Domain.Events;
using FlowTerminal.Domain.Instruments;
using FlowTerminal.OrderBook;
using Xunit;

namespace FlowTerminal.UiTests;

public class ReadOnlyDomTests
{
    private static readonly DateTime Ts = new(2024, 6, 3, 14, 0, 0, DateTimeKind.Utc);

    private static MarketEvent Depth(Side side, long price, long size) => MarketEvent.Quote(
        1, RootSymbol.NQ, "NQZ5", "CME Globex",
        side == Side.Bid ? MarketEventType.BidUpdate : MarketEventType.AskUpdate, Ts, Ts, side, price, size);

    private static MarketEvent Trade(long price, long qty, AggressorSide a) =>
        MarketEvent.Trade(1, RootSymbol.NQ, "NQZ5", "CME Globex", Ts, Ts, price, qty, a);

    [Fact]
    public void Builds_Observational_Ladder_With_Depth_And_Traded_Volume()
    {
        var book = new MarketByPriceOrderBook();
        book.Apply(MarketEvent.Control(1, RootSymbol.NQ, "NQZ5", "CME Globex", MarketEventType.SnapshotStart, Ts, Ts));
        book.Apply(Depth(Side.Bid, 100, 10));
        book.Apply(Depth(Side.Bid, 99, 20));
        book.Apply(Depth(Side.Ask, 102, 15));
        book.Apply(Depth(Side.Ask, 103, 25));
        book.Apply(MarketEvent.Control(1, RootSymbol.NQ, "NQZ5", "CME Globex", MarketEventType.SnapshotEnd, Ts, Ts));

        var profile = new VolumeProfile();
        profile.AddTrade(Trade(100, 5, AggressorSide.Buy));   // traded at ask @100
        profile.AddTrade(Trade(99, 7, AggressorSide.Sell));   // traded at bid @99

        var rows = ReadOnlyDom.Build(book, profile, levels: 3);

        Assert.Equal(7, rows.Count); // 104..98 inclusive
        var at100 = rows.First(r => r.PriceTicks == 100);
        Assert.Equal(10, at100.BidSize);
        Assert.Equal(5, at100.TradedAtAsk);
        Assert.Equal(5, at100.Delta);

        var at99 = rows.First(r => r.PriceTicks == 99);
        Assert.Equal(7, at99.TradedAtBid);
        Assert.True(at99.IsPoc); // 99 has the most traded volume
    }

    [Fact]
    public void Empty_Book_Yields_Empty_Ladder()
    {
        var rows = ReadOnlyDom.Build(new MarketByPriceOrderBook(), new VolumeProfile(), 5);
        Assert.Empty(rows);
    }

    [Fact]
    public void DomRow_Has_No_Order_Entry_Fields()
    {
        var names = typeof(DomRow).GetProperties().Select(p => p.Name).ToArray();
        foreach (var forbidden in new[] { "Submit", "Cancel", "Flatten", "Reverse", "WorkingOrder", "Position", "Pnl" })
        {
            Assert.DoesNotContain(names, n => n.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
        }
    }
}

public class TimeAndSalesTests
{
    private static readonly DateTime Ts = new(2024, 6, 3, 14, 0, 0, DateTimeKind.Utc);

    private static MarketEvent Trade(long qty, AggressorSide a, int sec = 0) =>
        MarketEvent.Trade(1, RootSymbol.NQ, "NQZ5", "CME Globex", Ts.AddSeconds(sec), Ts.AddSeconds(sec), 100, qty, a);

    [Fact]
    public void Latest_Returns_Newest_First()
    {
        var tape = new TimeAndSalesModel(capacity: 16);
        tape.Add(Trade(1, AggressorSide.Buy, 0));
        tape.Add(Trade(2, AggressorSide.Sell, 1));
        var latest = tape.Latest(10);
        Assert.Equal(2, latest[0].Quantity);
        Assert.Equal(1, latest[1].Quantity);
    }

    [Fact]
    public void Ring_Buffer_Bounds_Memory()
    {
        var tape = new TimeAndSalesModel(capacity: 16);
        for (int i = 0; i < 100; i++)
        {
            tape.Add(Trade(i + 1, AggressorSide.Buy, i));
        }

        Assert.Equal(16, tape.Count);
        Assert.Equal(100, tape.Latest(1)[0].Quantity); // most recent retained
    }

    [Fact]
    public void Large_Trades_Are_Flagged()
    {
        var tape = new TimeAndSalesModel(capacity: 16, largeTradeThreshold: 50);
        tape.Add(Trade(75, AggressorSide.Buy));
        Assert.True(tape.Latest(1)[0].IsLarge);
    }

    [Fact]
    public void Filter_Affects_Display_But_Not_Running_Volume()
    {
        var tape = new TimeAndSalesModel(capacity: 16) { Filter = new TapeFilter { MinSize = 10 } };
        tape.Add(Trade(5, AggressorSide.Buy));   // filtered out of display
        tape.Add(Trade(20, AggressorSide.Buy));  // shown
        Assert.Equal(1, tape.Count);
        Assert.Equal(25, tape.RunningVolume);     // all canonical trades counted
    }

    [Fact]
    public void Speed_Of_Tape_Detects_Acceleration()
    {
        var speed = new SpeedOfTape(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(60));
        // Sparse early, dense recently.
        for (int s = 0; s < 55; s += 10)
        {
            speed.Add(Trade(1, AggressorSide.Buy, s));
        }

        for (int i = 0; i < 20; i++)
        {
            speed.Add(Trade(1, AggressorSide.Buy, 56));
        }

        var result = speed.Compute(Ts.AddSeconds(58));
        Assert.True(result.AccelerationRatio > 1.0);
    }
}

public class WorkspaceStateTests
{
    [Fact]
    public void Json_Round_Trips()
    {
        var ws = new WorkspaceState
        {
            SelectedInstrument = "ES",
            ContractSymbol = "ESZ25",
            ChartInterval = "5m",
            ReducedMotion = true,
        };

        var restored = WorkspaceState.FromJson(ws.ToJson());
        Assert.Equal("ES", restored.SelectedInstrument);
        Assert.Equal("ESZ25", restored.ContractSymbol);
        Assert.Equal("5m", restored.ChartInterval);
        Assert.True(restored.ReducedMotion);
        Assert.Equal(ws.Panels.Count, restored.Panels.Count);
    }

    [Fact]
    public void Defaults_Are_Sensible()
    {
        var ws = new WorkspaceState();
        Assert.Equal("NQ", ws.SelectedInstrument);
        Assert.Equal("America/New_York", ws.DisplayTimeZone);
        Assert.Equal(30, ws.TargetFps);
        Assert.Contains(ws.Panels, p => p.Name == "MainChart");
    }
}
