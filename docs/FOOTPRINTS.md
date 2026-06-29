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

---

# Footprint Chart System (v0.37)

The footprint chart builds on the per-bar `Footprint` above with a fully-derived,
immutable `FootprintBar` (`FootprintAggregator`) and a richer renderer. It is strictly
**observational** — no order entry, ever. Prices are integer ticks; floating-point
prices are never used as keys.

## Aggressor side & Unknown volume
Aggressive **buys lift the ask** (ask volume); aggressive **sells hit the bid** (bid
volume). Provider aggressor flags are used as supplied. Trades with no determinable side
are tracked as **Unknown** volume — counted in a level's total but **excluded from
delta** (the previous code lost unknown volume; it is now a first-class bucket in
`VolumeProfile`). The synthetic feed always supplies a side, so mock footprints are in
the *Available* state; inferred sides would surface as *Estimated*.

## Display modes (`FootprintMode`)
Bid×Ask, Delta, Total Volume, Bid-only, Ask-only, Delta % (0 when empty — never NaN/∞),
Trade Count, Volume-Profile silhouette. Cycled from the toolbar **FP** button; persisted
in templates.

## Derived calculations
- **Bar/level delta** = ask − bid.
- **POC** = max of the selected metric (total/bid/ask/|delta|/trade-count). **Tie rule**:
  closest to close → higher (bull) / lower (bear) → higher price. Deterministic.
- **Diagonal imbalance** — buy: `ask(P) ≥ ratio·bid(P−1)`; sell: `bid(P) ≥ ratio·ask(P+1)`.
  Default 300%, independent buy/sell, with an explicit **zero-denominator policy**
  (Ignore / RequireMinNumerator / ExtremeImbalance / DenominatorFloor) — no silent divide.
- **Horizontal imbalance** (optional) — ask(P) vs bid(P) at the same price.
- **Stacked imbalance** — ≥ N consecutive same-side imbalance levels (default 3, optional
  one-gap), reported as zones and shaded faintly.
- **Zero print** — one side 0 while the opposite ≥ min; bar extremes excludable.
- **Unfinished auction** — bid at the high / ask at the low ≥ min; **Developing** while
  the bar is live, **Confirmed** once closed.
- **Large trade** — level's largest single print ≥ threshold (Fixed default; NQ 45 / ES 75).
- **Bar stats** — total/bid/ask/unknown volume, delta, trade count, POC, max ± level
  delta, imbalance + stacked counts.

## Settings, presets, persistence
`FootprintSettings` is a single validated record (`Validate()` clamps every value so no
ratio, threshold, or floor can be invalid). `FootprintSettings.Nq` / `.Es` give
per-instrument presets, applied on contract selection while preserving the chosen mode.
The display mode round-trips through chart templates (back-compatible: older templates
default to Bid×Ask).

## Replay determinism
Each `FootprintBar` carries an **FNV-1a content hash** over OHLC, per-level
bid/ask/delta, POC, and all flags. Aggregation is order-independent and a pure function
of trades + settings; repeated synthetic replay reproduces identical hashes (tested
end-to-end).

## Rendering & LOD
One Skia canvas sharing the candle viewport. Detail adapts to zoom: numbers (tall rows)
→ coloured bid/ask bars (smaller) → candle + POC silhouette (sub-pixel). Green ask
(right), light-purple bid (left); POC, imbalance outlines, zero-print marks, large-trade
emphasis, stacked zones and a green/purple delta footer — existing palette only, **no
red / thermal / rainbow**.

## Known limitations
- Time bars are the validated aggregation path; volume/tick/range reuse the chart's bar
  aggregator (the footprint engine is bar-type agnostic).
- `LargeTradeCount` is a presence flag (max single print ≥ threshold); session-percentile
  thresholds are configurable but default to Fixed.
- True (MBO) iceberg confirmation needs order-level data the MBP feed lacks; surfaced as
  *Estimated* via the detector suite.

## Interpretation
These features describe what already traded — **not predictions**, no profit claim.

## Tests added
`FootprintAggregatorTests` (12 reference/behaviour fixtures: levels, POC + tie,
diagonal imbalance + zero-denominator policy, zero prints, unfinished auctions, stacked,
large trades, order-independence/hash, delta-% safety, empty bar). `FootprintRichRenderTests`
(rich render green/purple/no-thermal, every mode renders, synthetic replay hash
determinism). `ChartOverlayRenderTests` updated to the `FootprintBar` model.
