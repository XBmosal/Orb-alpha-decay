# DOM status

Legend: ✅ done · 🟡 partial / data-limited · ⬜ next phase.

| Feature | Implemented | Tested | Reconciled | Data limit / notes |
|---|---|---|---|---|
| Consumes canonical MBP book (shared with heatmap) | ✅ | ✅ | ✅ | same book/profile |
| Integer-tick prices, tick-aligned descending rows | ✅ | ✅ | — | |
| Best bid / best ask + flags | ✅ | ✅ | ✅ | reconciles with book |
| Crossed-book never produced | ✅ | ✅ | ✅ | book enforces bid<ask |
| Bid / ask size | ✅ | ✅ | — | |
| Cumulative depth (touch-outward) | ✅ | ✅ | — | **fixed inversion bug** |
| Executed buy/sell volume + delta | ✅ | ✅ | ✅ | reconciles with profile |
| Distance from touch | ✅ | ✅ | — | |
| Pulling / stacking | ✅ | ✅ | — | Estimated (MBP); now wired into the feed |
| Replenishment count | ✅ | ✅ | — | Estimated (MBP) |
| Liquidity wall flag | ✅ | ✅ | — | ≥3× visible median + floor (NQ 150 / ES 300) |
| POC / value-area flags | ✅ | — | — | from profile |
| Column registry + capability requirements | ✅ | ✅ | — | MBP/MBO/Trades flags |
| 10 built-in presets (protected) | ✅ | ✅ | — | MBO preset capability-gated |
| MBO order-level columns | 🟡 | ✅ (gated) | — | require native MBO; inactive on MBP feed |
| Skia ladder renderer (depth bars, best bid/ask, wall outlines, POC tint) | ✅ | ✅ | — | replaces the WPF ItemsControl; preset-driven columns |
| Preset rendering in-app (9 MBP presets) + cycle button | ✅ | ✅ | — | toolbar "DOM:" button; persists in templates |
| READ ONLY label in panel header | ✅ | — | — | |
| Preset persistence (templates) | ✅ | — | — | mirrors footprint preset |
| Interactive column builder (show/hide/reorder/resize) | ⬜ | — | — | next phase (model ready) |
| Per-column settings panel | ⬜ | — | — | next phase |
| Auto-center modes / return-to-market / freeze view | ⬜ | — | — | next phase |
| Row inspector / context menu / DOM tooltips | ⬜ | — | — | next phase |
| Replay hash for DOM analytics | ⬜ | — | — | book/profile already deterministic |

## Defects fixed this pass
1. **Cumulative depth inversion** — `ReadOnlyDom` summed from the far edge inward despite
   its own "inside (best) outward" comment. Now correctly touch-outward.
2. **`PullStackTracker` was dead code** — never fed or consumed. Now wired into the feed
   (fed on every depth event, reset per session) and surfaced on each `DomRow`.

## Guarantees
- Read-only: no order entry/quantity/position/P&L; no execution controls added.
- Green/light-purple identity preserved (bid green / ask light purple).
- Canonical book unchanged; DOM consumes a snapshot, never mutates it.
- Estimated analytics (pull/stack/refill) are labelled and documented, not presented as
  exact MBO facts.
