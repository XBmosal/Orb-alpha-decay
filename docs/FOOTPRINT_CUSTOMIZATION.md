# Footprint customization

The footprint is a **modular order-flow charting system**, not one fixed indicator.
A footprint is configured along three independent axes — **data mode** (what each cell
measures), **visual layout** (how the cell is drawn), and **background/overlays** (extra
shading and order-flow marks) — bundled into named **presets**. It remains strictly
observational; nothing here places or simulates an order, and the global theme and
green/light-purple semantics are unchanged.

## Separation of concerns

| Layer | Type | Changing it… |
|---|---|---|
| Data mode | `FootprintMode` | **recalculates** what cells measure |
| Visual layout | `FootprintVisualLayout` | **visual only** — no recompute |
| Background source / normalization | `FootprintBackground` / `FootprintNormalization` | **visual only** |
| Candle, opacity, separator, footer | `FootprintSettings` visual fields | **visual only** |
| Order-flow thresholds (imbalance, POC source, large trade, zero print) | calculation fields | **recalculates** flags |

The aggregator (`FootprintAggregator`) reads only calculation fields, so any visual
change leaves the bar — and its replay hash — identical (test-enforced).

## Data modes (`FootprintMode`)
Bid×Ask, Delta, Total Volume, Bid-only, Ask-only, Delta %, Trade Count, Volume Profile.
All read from the same canonical `FootprintBar`.

## Visual layouts (`FootprintVisualLayout`)
SplitText, SingleValue, SplitCell, Histogram, MirroredHistogram, ProfileCandle,
GradientCell, TextOnly, OutlineCell, Marker, Ladder, Hybrid.

## Compatibility rules (`FootprintCompatibility`)
Not every layout fits every mode (e.g. a single mirrored bar makes no sense for Bid×Ask
text). `AllowedLayouts(mode)` lists valid layouts; `Resolve(mode, layout)` snaps an
incompatible pair to the mode's default, so a broken chart is impossible — incompatible
combinations are disabled, never silently mis-rendered.

## Background shading (`FootprintBackground` × `FootprintNormalization`)
Cells can be shaded by bid/ask/total volume, delta, |delta|, delta %, or trade count.
Delta/percent shade green (positive) or light purple (negative); volume/trade-count use
neutral grey. Normalization is **PerBar**, **VisibleRange**, **Logarithmic**, or
**SquareRoot** — stable maxima avoid flicker. `CellOpacity` scales intensity.

## Composable layers (render order)
candle body/wick → stacked-imbalance zones → cell background → POC → layout fill
(bars/profile) → imbalance outlines / zero-print marks / large-trade emphasis → cell
text → bar-delta footer. Each is gated by settings and by a level-of-detail threshold
(numbers when rows are tall, bars when smaller, candle+POC silhouette at sub-pixel rows).

## Built-in presets (`FootprintPresetRegistry`, 12, protected)
Classic Bid×Ask · Bid×Ask Heat · Delta Footprint · Delta Profile · Volume Profile ·
Bid/Ask Profile · Minimal · Order-Flow Ladder · Large Trades · Imbalance Map ·
Volume + Delta Hybrid · Clean Replay Study. Each is a distinct, professional
combination of mode + layout + background + overlays. Cycle them from the toolbar **FP**
button; the choice persists in chart templates.

## NQ / ES overrides
`FootprintPreset.ForInstrument(root)` keeps the preset's visual fields and substitutes
the instrument's calculation thresholds (large-trade 45 NQ / 75 ES; min imbalance volume
2 / 4). Switching instruments re-applies the chosen preset for the new contract without
discarding the user's preset selection.

## Replay & data
Every preset works identically across synthetic, recorded, replay, and (eventual)
Rithmic data because only calculation fields affect the bar. Aggressive buys = ask
volume, sells = bid volume; unknown-aggressor volume is tracked separately and excluded
from delta. Inferred sides surface as *Estimated*.

## Known limitations
- Full settings **panel** UI (per-field numeric controls, live preview, import/export)
  is the next sub-phase; presets + per-preset settings + template persistence + the
  toolbar cycle are implemented now.
- Custom user-defined presets are not yet persisted separately from templates.
- Session-percentile large-trade thresholds are configurable but default to Fixed.

## Interpretation
These views describe what already traded — **not predictions**, no profit claim.
