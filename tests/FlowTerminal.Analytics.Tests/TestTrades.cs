using FlowTerminal.Domain.Events;
using FlowTerminal.Domain.Instruments;

namespace FlowTerminal.Analytics.Tests;

/// <summary>Shared helpers for building deterministic trade fixtures.</summary>
internal static class TestTrades
{
    public static readonly DateTime Base = new(2024, 6, 3, 14, 0, 0, DateTimeKind.Utc);

    public static MarketEvent Trade(DateTime ts, long priceTicks, long qty, AggressorSide aggressor) =>
        MarketEvent.Trade(1, RootSymbol.NQ, "NQZ5", "CME Globex", ts, ts, priceTicks, qty, aggressor,
            flags: MarketEventFlags.AggressorSupplied);

    public static MarketEvent At(int second, long priceTicks, long qty, AggressorSide aggressor) =>
        Trade(Base.AddSeconds(second), priceTicks, qty, aggressor);
}
