# Footprint status

Legend: ✅ done · 🟡 partial / data-limited · ⬜ pending.

| Feature | Implemented | Tested | Replay verified | Rendering verified | Perf | Data limits / notes |
|---|---|---|---|---|---|---|
| Canonical trade aggregation | ✅ | ✅ | ✅ | ✅ | ✅ | one engine for live/mock/replay |
| Integer-tick price levels | ✅ | ✅ | — | — | — | no float keys |
| Bid/ask/**unknown** volume | ✅ | ✅ | ✅ | ✅ | — | unknown bug fixed |
| Bar & level delta | ✅ | ✅ | ✅ | ✅ | — | ask − bid |
| Delta % (safe) | ✅ | ✅ | — | ✅ | — | 0 when empty |
| POC + deterministic tie rule | ✅ | ✅ | ✅ | ✅ | — | closest-to-close |
| POC source (vol/bid/ask/|Δ|/trades) | ✅ | ✅ | — | — | — | setting |
| Diagonal imbalance | ✅ | ✅ | ✅ | ✅ | — | 300% default |
| Zero-denominator policy | ✅ | ✅ | — | — | — | 4 modes |
| Horizontal imbalance | ✅ | ✅ | — | ✅ | — | distinct, optional |
| Stacked imbalance + zones | ✅ | ✅ | ✅ | ✅ | — | ≥3, optional 1-gap |
| Zero prints | ✅ | ✅ | ✅ | ✅ | — | extremes excludable |
| Unfinished auctions (dev/confirmed) | ✅ | ✅ | ✅ | ✅ | — | high/low |
| Large trades | ✅ | ✅ | ✅ | ✅ | — | Fixed; NQ 45 / ES 75 |
| Bar statistics | ✅ | ✅ | ✅ | ✅ | — | delta footer + counts |
| Display modes (8) | ✅ | ✅ | — | ✅ | — | toolbar cycle, persisted |
| Candle body behind cells | ✅ | — | — | ✅ | — | faint green/purple |
| Volume-profile silhouette | ✅ | ✅ | — | ✅ | — | mode |
| Level-of-detail (zoom) | ✅ | ✅ | — | ✅ | — | numbers→bars→silhouette |
| Pan / zoom / price-row align | ✅ | ✅ | — | ✅ | — | shared viewport |
| Settings model + validation | ✅ | ✅ | — | — | — | `FootprintSettings.Validate()` |
| NQ / ES presets | ✅ | ✅ | — | — | — | applied on contract |
| Template persistence (mode) | ✅ | — | — | — | — | back-compatible |
| Replay determinism (hash) | ✅ | ✅ | ✅ | — | — | FNV-1a per bar |
| Crosshair / tooltip hit-testing | ⬜ | — | — | — | — | chart crosshair exists; per-cell tooltip pending |
| Settings panel UI | 🟡 | — | — | — | — | data layer + presets + mode toggle done; full panel pending |
| Volume/tick/range footprint bars | 🟡 | — | — | — | — | engine bar-type agnostic; time bars validated |
| Session-percentile large threshold | 🟡 | — | — | — | — | configurable; defaults to Fixed |

## Cross-cutting guarantees
- No NaN/∞ (guarded delta %, divide-safe imbalances). · Integer-tick keys. · Incremental
  active bar; closed bars stable. · Order-independent aggregation; deterministic hash. ·
  Bounded per-bar state. · UI theme/colours unchanged; green/purple semantics preserved. ·
  No execution controls.

## Known defects (ranked)
1. *(low)* Per-cell tooltip/crosshair readout not yet wired (chart-level crosshair only).
2. *(low)* Full footprint settings **panel** UI pending (mode toggle + presets + template
   persistence are in; numeric controls for ratio/threshold are data-layer only).
3. *(low)* Session-percentile large-trade thresholds default to Fixed.
