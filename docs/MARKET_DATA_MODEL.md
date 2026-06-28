# Market Data Model

## Prices are integer ticks
Internal prices are **never** binary floating point. Every price is stored as an
integer number of ticks:

```
priceTicks    = round(exchangePrice / tickSize)   // banker's rounding (ToEven)
exchangePrice = priceTicks * tickSize
```

`decimal` is used only at the display / currency / export / settings boundary.
See `FlowTerminal.Domain.Instruments.PriceConverter`.

| Root | Tick size | Point value | Tick value | Exchange |
|------|-----------|-------------|-----------|----------|
| NQ | 0.25 pt | $20 | $5.00 | CME Globex |
| ES | 0.25 pt | $50 | $12.50 | CME Globex |

## Canonical event
`FlowTerminal.Domain.Events.MarketEvent` is a `readonly struct` (allocation-
efficient; copyable by value into pooled buffers). All times are UTC. Fields:

`InstrumentId, Root, ContractSymbol, Exchange, ExchangeSequence,
ExchangeTimestampUtc, ReceiveTimestampUtc, Type, Side, PriceTicks, Quantity,
OrderId, TradeId, Aggressor, Flags, Source`.

### Event types
`Add, Modify, Cancel, Execute, Trade, BidUpdate, AskUpdate, SnapshotStart,
SnapshotEnd, BookClear, SessionStatus, InstrumentDefinition, ConnectionStatus,
SequenceGap`.

### Flags
`AggressorSupplied, AggressorInferred, Implied, Snapshot, HasExchangeSequence,
HasExchangeTimestamp, Synthetic`. Inferred aggressor is distinguished from
exchange-supplied so the UI can label inferred values **Estimated**.

## Capabilities
`ProviderCapabilities` advertises what a feed honestly supports: `Trades,
TopOfBook, MarketByPriceDepth, MarketByOrderDepth, HistoricalTrades,
HistoricalBars, HistoricalDepth, ExchangeTimestamps, SequenceNumbers,
ImpliedLiquidityFlags, AggressorSideFlags`. Features depending on an absent
capability are disabled gracefully (e.g. queue analysis needs MBO), and values
derived without exchange support are labelled **Estimated**.

## Determinism
The synthetic generator uses a self-contained xorshift64\* PRNG
(`DeterministicRng`) — not `System.Random` — so the same seed yields byte-for-byte
identical streams across runtimes. This underpins reproducible mock data, replay,
and hash-based replay validation.
