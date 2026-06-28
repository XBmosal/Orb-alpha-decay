# Footprints & Core Analytics

**Status: Complete · Tested** (Phase 3). All math is computed from the canonical
trade stream — identical code for live and replay (proven by
`AnalyticsDeterminismTests`). Prices are integer ticks; "one tick" = ±1 in tick
units.

## Bars (`FlowTerminal.Analytics.Bars`)
- **Time** bars align to fixed UTC interval boundaries; a trade in a later bucket
  closes the developing bar.
- **Tick** bars close every N trades.
- **Volume** bars close once accumulated volume reaches the threshold (the
  breaching trade is included).
- **Range** bars close once the high−low span reaches the configured tick range.

Each bar carries OHLC (ticks), volume, buy/sell volume, trade count, and
`Delta = BuyVolume − SellVolume`. Buy/sell volume split by trade aggressor;
unknown-aggressor trades count toward volume but not buy/sell/delta.

## Delta & CVD (`FlowTerminal.Analytics.Delta`)
`CvdCalculator` maintains cumulative volume delta in O(1) per trade
(buy adds size, sell subtracts, unknown is neutral) and tracks max positive /
negative excursions.

## Volume Profile (`FlowTerminal.Analytics.Profiles`)
Volume-at-price with buy/sell split. Derived on demand (so "developing" POC/VA is a
query at the current state):
- **POC** — price with greatest total volume (ties → higher price).
- **Value area** — expands outward from POC, repeatedly taking the higher-volume
  neighbouring level until cumulative volume reaches `percent` (default 70%);
  returns POC/VAL/VAH. Empty profiles are safe (no POC).

## Footprints (`FlowTerminal.Analytics.Footprints`)
Per-bar bid/ask volume at price (aggressive buys = ask volume; aggressive sells =
bid volume) with POC, value area, per-level delta, and imbalance detection.

### Diagonal imbalance (definition)
- **Buy imbalance at price P:** ask volume at P vs **bid volume at P−1**.
- **Sell imbalance at price P:** bid volume at P vs **ask volume at P+1**.

A level qualifies when the dominant side ≥ `Ratio × compared` **and** ≥
`MinVolume`. Buy and sell ratios are independent. Zero denominators are handled
safely: a non-zero dominant side over zero compared volume qualifies when it meets
`MinVolume` — the code never divides by zero and never produces NaN/Infinity.

### Horizontal imbalance
Compares bid vs ask volume at the **same** price level, using the same ratio /
min-volume rules.

## VWAP (`FlowTerminal.Analytics.Vwap`)
Volume-weighted average price with standard-deviation bands, computed incrementally
(`variance = Σ(v·p²)/Σv − vwap²`, volume-weighted population variance, guarded
against floating-point negatives). Daily / weekly / monthly / anchored VWAP differ
only in when `Reset()` (the anchor) is called. Empty VWAP returns NaN safely.

## Tests (19, all passing)
Exact-fixture tests for time/tick/volume/range bars, CVD (incl. unknown
aggressor), volume-profile POC/value-area/delta, footprint bid-ask mapping,
diagonal imbalance (incl. zero-denominator), horizontal imbalance, VWAP value/
std-dev/bands, plus the analytics-determinism test.
