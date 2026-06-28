using FlowTerminal.Domain.Events;
using FlowTerminal.Domain.Instruments;
using Xunit;

namespace FlowTerminal.Domain.Tests;

public class MarketEventTests
{
    private static readonly DateTime Ts = new(2024, 6, 3, 14, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Trade_Factory_Populates_Fields()
    {
        var e = MarketEvent.Trade(
            instrumentId: 1, root: RootSymbol.NQ, contractSymbol: "NQZ5", exchange: "CME Globex",
            exchangeTsUtc: Ts, receiveTsUtc: Ts, priceTicks: 80000, quantity: 7,
            aggressor: AggressorSide.Buy, exchangeSequence: 42, tradeId: 99,
            flags: MarketEventFlags.AggressorSupplied);

        Assert.Equal(MarketEventType.Trade, e.Type);
        Assert.Equal(80000, e.PriceTicks);
        Assert.Equal(7, e.Quantity);
        Assert.Equal(AggressorSide.Buy, e.Aggressor);
        Assert.Equal(42, e.ExchangeSequence);
        Assert.Equal(99, e.TradeId);
        Assert.True(e.HasFlag(MarketEventFlags.AggressorSupplied));
    }

    [Fact]
    public void Quote_Factory_Rejects_NonQuote_Types()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MarketEvent.Quote(
            1, RootSymbol.ES, "ESZ5", "CME Globex", MarketEventType.Trade, Ts, Ts, Side.Bid, 1, 1));
    }

    [Fact]
    public void MarketEvent_Is_Value_Type()
    {
        // Guards the allocation-efficiency design decision.
        Assert.True(typeof(MarketEvent).IsValueType);
    }
}
