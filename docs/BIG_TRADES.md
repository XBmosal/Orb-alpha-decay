# Big Trades

Visualizes unusually large **aggressive** market orders as bubbles placed at their
executed price and time. It describes observed aggression — it never asserts who traded
or predicts direction, and it adds **no** order-entry surface of any kind.

## Read-only

Big Trade bubbles are analytical markers only. There are no buy/sell/quantity/position
controls and no execution or simulated execution. (Interactions such as tooltips,
inspectors, bookmarks, and Time-and-Sales highlighting are read-only and are staged as a
follow-up; see the status doc.)

## Data source

Big Trades consume the one canonical `MarketEvent` trade stream — the same events that
drive Time & Sales, footprints, DOM executed volume, the heatmap, CVD, and volume
statistics. There is no separate Big-Trades feed, so every visualization reconciles
against one detector. Prices are integer ticks; time is UTC.

## Aggressor classification

The shared `AggressorClassifier` prefers the feed-supplied aggressor side and falls back
through a documented, deterministic hierarchy only when it is missing:

1. **Provider-supplied side** → *Native*.
2. Price **at/through the best ask** → Buy · **at/through the best bid** → Sell — *estimated (bid/ask)*.
3. **Inside the spread** → tick rule (uptick = buy, downtick = sell) — *estimated (tick)*.
4. **Unchanged price** → inherit the last classified side — *estimated (inherited)*.
5. Otherwise → **Unknown**.

When the order book is unreliable, bid/ask inference is **paused**: a provider side is
still honoured, otherwise the trade is **Unknown** — never silently guessed. Every trade
carries a **classification quality** (Native / InferredBidAsk / InferredTick / Inherited /
Unknown / InvalidBook); anything inferred is surfaced as **Estimated** (dashed bubble
outline). Unknown trades are drawn **neutral gray** — they are never counted as sells.

## Threshold modes

A trade or aggregated group is "big" only when it meets the configured rule. Every result
reports the threshold it used, so the decision is transparent.

| Mode | Rule |
|---|---|
| **Fixed** | total ≥ a fixed contract count (separate buy/sell allowed) |
| **RollingPercentile** | total **strictly exceeds** a percentile of recent trade sizes (rolling window) |
| **SessionPercentile** | as above, over all trades this session (resets per session) |
| **ZScore** | total ≥ rolling mean + k·σ |
| **RelativeToAverage** | total ≥ a multiple of the rolling average trade size |
| VisibleRangePercentile / RelativeToVolume / Adaptive | *staged* — fall back to RollingPercentile |

An **absolute floor** always applies, and adaptive modes report a **warm-up** state until
`MinSamples` trades have been seen (falling back to the fixed threshold meanwhile).
Percentile modes require *strictly exceeding* the percentile value, so a stream of
identical sizes flags nothing.

## Aggregation

Related aggressive prints are grouped (configurable): **None** (raw — one bubble per
trade), **SamePrice**, **AdjacentPrice** (within a tick distance), or **Sweep**. A group
breaks on a side change, a time-window gap, a price outside the rule, or the maximum
duration. Each group carries: weighted-average price, min/max price, total, trade count,
largest child, duration, sequence range, a deterministic id, and classification quality.

## Sweep detection

A group is flagged a **sweep** only when it executed **monotonically** across at least
`SweepMinLevels` distinct consecutive levels in the aggressive direction and cleared
`SweepMinVolume`. A large single-level print is **not** a sweep. Sweeps get a bolder
outline and an optional price capsule spanning their executed range.

## Bubble sizing

Bubble **area** tracks volume through a nonlinear map (`√` by default, or `log`),
normalized to the largest visible group and clamped to a min/max radius — so large prints
don't dominate the chart and ordinary qualifying prints stay visible. Radius scaling is
never linear (area would grow quadratically).

## Visual identity

- Aggressive **buys → Flow Terminal green**; **sells → light purple**; **unknown → neutral gray**.
- **Estimated** classification → dashed outline. **Sweeps / very large** → bolder outline.
- Styles: Solid, Ring, Soft, Dot, Outline+Quantity. Compact contract labels (950, 1.2k,
  18k) with contrast-aware colour, shown when the bubble is large enough.
- Deterministic z-order: small first, sweeps and very large last. No red, no neon, no
  thermal/rainbow palette.

## Heatmap integration

The liquidity heatmap emphasises the **shared** Big Trade groups over its faint
all-trade context, using the heatmap's own time/price mapping so they align exactly. The
**Large/Block Trade** study toggle gates them. The heatmap's volume histogram and context
bubbles now bucket buy/sell/**unknown** (unknown neutral), reconciling with the engine.

## Replay & determinism

The engine is single-writer and deterministic: the same recorded trades + settings
reproduce identical groups and ids. `BigTradeDetector.Hash` produces a stable, order-
sensitive hash of a group sequence for replay verification.

## NQ / ES presets

Eight built-in presets (all editable): Balanced Bubbles, Large Only, Detailed Tape, Sweep
Detector, Adaptive Session, **NQ Balanced**, **ES Balanced**, Replay Study.
`BigTradePresetRegistry.ForInstrument` seeds NQ/ES-appropriate defaults.

## Interpretation (no profit claims)

Bubbles describe *aggressive execution*: "large aggressive buy", "multi-level sweep",
"estimated aggressor side". They do **not** mean "smart money", "institution buying",
"accumulation", or "guaranteed reversal". A large trade is not inherently meaningful, and
size does not predict direction.

## Known limitations / staged work

- VisibleRange / RelativeToVolume / Adaptive threshold modes are staged (fall back to
  RollingPercentile).
- Bubble tooltip, inspector, alerts, and Time-and-Sales/footprint/DOM reconciliation
  surfacing are staged; the shared engine they build on is in place and exposed on the
  snapshot. See `BIG_TRADES_STATUS.md`.
