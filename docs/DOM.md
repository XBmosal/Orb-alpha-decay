# Read-only DOM (depth of market)

The DOM is a **strictly observational** depth ladder. It has no order entry, quantity,
working-order, position, or P&L fields, and never will — clicking a row only performs
safe analytical actions. It is labelled **READ ONLY**.

## Data source

The DOM consumes the **same canonical `MarketByPriceOrderBook`** the heatmap uses, plus
the shared session `VolumeProfile` for executed volume — there is no separate DOM-only
book. Prices are integer ticks (NQ/ES tick 0.25); floating-point prices are never used
as keys. Best bid/ask, depth, and validity therefore reconcile with the heatmap by
construction (tested).

## Columns & calculations

`ReadOnlyDom.Build` produces one immutable `DomRow` per tick around the mid:

- **Bid / Ask size** — resting displayed size at the level (from the book).
- **Cumulative depth** — accumulates **from the touch outward**: cumulative ask at
  price P (≥ best ask) = sum of ask sizes from the best ask up to P; cumulative bid at
  P (≤ best bid) = sum from the best bid down to P. *(This corrected a prior inversion
  where it summed from the far edge inward.)*
- **Executed volume** — buys (traded at ask) and sells (traded at bid) from the
  trade-driven profile; **never inferred from depth changes**. `Delta = buys − sells`,
  plus total traded.
- **Best bid / best ask flags** and **distance from touch** (ticks).
- **Pulling / stacking / replenishment** — from `PullStackTracker`, which measures
  changes in *displayed* size only (pull = size removed, stack = size added, refill =
  restored after depletion). These are **observational and Estimated** under
  market-by-price: they describe displayed-size dynamics, not intent, and do not
  attribute a reduction to a cancel when a trade explains it at MBP granularity.
- **Liquidity wall flag** — a level whose size is unusually large for its side
  (≥ 3× the visible median) and clears an absolute floor (NQ 150 / ES 300 by default).
- POC / value-area flags (from the profile).

## Capability states (MBP vs MBO)

The synthetic and (eventual) Rithmic MBP feeds carry depth-by-price, so size,
cumulative, executed volume, pulling/stacking, replenishment and walls are available —
the latter two **labelled Estimated**. Order-level columns (order count, largest order)
require **native MBO** data; their descriptors carry `DomDataRequirement.Mbo` so the UI
can badge them and present "MBO data required" instead of fake zeros. The
`DomColumnRegistry` exposes each column's requirement and `DomPreset.RequiresMbo` flags
presets that need it.

## Column / preset model

`DomColumnRegistry` is the catalogue of available columns (id, header, full name, side,
data requirement, default width, alignment, estimated flag). `DomPresetRegistry` ships
**ten protected built-in presets** — Classic Depth, Order Flow, Pulling & Stacking,
Liquidity Analysis, Executed Volume, Replenishment, MBO Analytics, Compact Ladder, Full
Professional, Replay Study — each an ordered list of column types around the central
price column.

`DomLayout` is the editable layer on top of a preset: it holds **every** known column
with a visibility flag, an explicit order, and a width. `FromPreset` seeds it (preset
columns visible first, the rest hidden); `ResolveColumns` / `ResolveWidths` produce
exactly what the Skia renderer consumes. It serialises to a compact `id:visible:width`
string for template persistence and deserialises defensively (unknown ids skipped, new
columns back-filled hidden).

## Column builder (UI)

The Order Book header's **"DOM:"** button opens a popup editor:

- **Preset chips** pick a base layout (the active preset is highlighted).
- Each column row toggles **show/hide** (click the row), **reorders** (▲▼), and
  **resizes** (−/＋, clamped 36–220 px). Estimated and MBO columns are tagged.
- Once a layout is hand-edited the button reads `DOM: <preset>*` and the customised
  layout (not just the preset name) is saved into the chart template.

The editor is strictly presentational — it only changes which analytical columns the
ladder shows and how wide they are. There are no order-entry surfaces, and the popup is
itself labelled READ ONLY.

## Study aids (freeze + inspector)

Two read-only aids sit on the ladder:

- **Freeze** (header toggle) holds the ladder so a level can be studied while the market
  moves. A `❄ FROZEN` chip marks the held state; live bid/ask/spread readouts keep
  updating. Editing the column layout still works while frozen. Freezing acts on the view
  only — it does not pause the feed or touch the market.
- **Row inspector**: hovering a row outlines it and shows a bottom strip with that price's
  full breakdown (price, bid/ask size, cumulative depth, executed buy/sell, delta, wall
  flag). It is informational only — there is no context menu or order action.

## Determinism

`ReadOnlyDom.Hash` returns a 64-bit FNV-1a hash of a DOM snapshot's observable fields in
row order. It is deterministic and order-sensitive, so a replay can assert the DOM
reconstructed identically from the same recorded book/trade data.

## Reconciliation

Tested invariants: DOM best bid/ask equal the book's; the book is never crossed; rows
are exactly tick-aligned and descending; no negative sizes; cumulative is touch-outward;
executed volume equals the profile's buy/sell volume at each price.

## NQ / ES defaults

Wall floor 150 (NQ) / 300 (ES). Tick 0.25 both; point value $20 (NQ) / $50 (ES).

## Known limitations / next phase

- Pulling/stacking/replenishment are **Estimated** from MBP. They are surfaced as
  dedicated DOM columns (Pull/Stk, Refill) in the relevant presets and editable via the
  column builder.
- The per-column settings panel and DOM-specific keyboard shortcuts are scoped as the
  next phase; the data/analytics foundation they build on is in place.
- Native MBO analytics are capability-gated and inactive on the MBP feed (by design).

## Interpretation

The DOM describes displayed liquidity and executed flow — **not predictions**, and a
wall is not a promise of support/resistance. No profit claims.
