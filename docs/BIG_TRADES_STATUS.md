# Big Trades status

Legend: ✅ done · 🟡 partial / data-limited · ⬜ staged (next phase).

| Feature | Implemented | Tested | Replay-verified | Rendering-verified | Reconciled | Notes |
|---|---|---|---|---|---|---|
| Canonical trade events drive detection | ✅ | ✅ | ✅ | — | ✅ | one `MarketEvent` stream |
| Aggressor: prefer native side | ✅ | ✅ | ✅ | — | — | |
| Aggressor: bid/ask + tick + inherit fallback | ✅ | ✅ | ✅ | — | — | labelled Estimated |
| Invalid-book inference paused | ✅ | ✅ | — | — | — | provider side still honoured |
| Unknown handled (neutral, never sell) | ✅ | ✅ | — | ✅ | ✅ | **fixed Unknown→sell bug** |
| Classification quality tracked | ✅ | ✅ | ✅ | ✅ | — | Native/Inferred/Unknown/InvalidBook |
| Fixed threshold | ✅ | ✅ | — | — | — | separate buy/sell |
| Rolling percentile | ✅ | ✅ | ✅ | — | — | strict-exceeds |
| Session percentile | ✅ | ✅ | — | — | — | resets per session |
| Z-score | ✅ | ✅ | — | — | — | warm-up gated |
| Relative-to-average | ✅ | ✅ | — | — | — | |
| Visible-range / relative-to-volume / adaptive | ⬜ | — | — | — | — | fall back to rolling percentile |
| Raw (per-trade) mode | ✅ | ✅ | — | — | — | aggregation = None |
| Aggregation: same/adjacent/sweep + window | ✅ | ✅ | ✅ | — | — | weighted price, largest child |
| Weighted-average price / min-max / largest | ✅ | ✅ | — | — | — | |
| Sweep detection (monotonic, multi-level) | ✅ | ✅ | ✅ | ✅ | — | single-level ≠ sweep |
| Nonlinear bubble sizing (√/log, clamp) | ✅ | ✅ | — | ✅ | — | normalized to visible max |
| Bubble styles (solid/ring/soft/dot/outline) | ✅ | ✅ | — | ✅ | — | green/purple/neutral |
| Estimated dashed outline · sweep capsule | ✅ | ✅ | — | ✅ | — | |
| Compact labels + z-order | ✅ | ✅ | — | ✅ | — | |
| Deterministic group ids + replay hash | ✅ | ✅ | ✅ | — | — | `BigTradeDetector.Hash` |
| Diagnostics counters | ✅ | ✅ | — | — | — | on the snapshot |
| NQ / ES presets (8 built-ins) | ✅ | ✅ | — | — | — | editable |
| Live wiring + snapshot exposure | ✅ | ✅ | — | ✅ | ✅ | `ShowBigTrades`, gated by LT study |
| Heatmap integration (shared groups) | ✅ | ✅ | — | ✅ | ✅ | aligned via heatmap mapping |
| Candle-chart overlay | ✅ | ✅ | — | ✅ | — | bubbles placed in the bar they executed in (intra-bar time offset); gated by LT study |
| Settings panel + live preview | ⬜ | — | — | — | — | model + presets ready |
| Tooltip / inspector / pin | ⬜ | — | — | — | — | group model carries the fields |
| Alerts (read-only) | ⬜ | — | — | — | — | |
| Time & Sales / footprint / DOM reconciliation | ⬜ | — | — | — | (engine ✅) | shared engine in place; surfacing pending |

## Defects fixed this pass
1. **Unknown aggressor → counted as sell.** `TradeDot` used `IsBuy = Aggressor == Buy`, so
   every unknown-side trade fell into the sell bucket (bubbles and volume histogram). Now
   `TradeDot` carries the classified `AggressorSide` and the heatmap renders unknown
   neutral gray. *(Critical, data integrity.)*
2. **Three unrelated "large/aggressive" notions** (LargeTradeDetector, SweepDetector,
   heatmap-all-trade bubbles) that never reconciled. The heatmap now emphasises the shared
   `BigTradeDetector` groups. *(High, duplication.)*

## Guarantees
- Read-only: no order entry/quantity/position/P&L/execution surfaces were added.
- Green / light-purple / neutral identity preserved; no red, neon, or thermal palette.
- Canonical events unchanged; the engine consumes them and never fabricates a trade.
- Estimated/inferred classification is labelled, not presented as exchange-confirmed fact.
