# Feature Status

Status legend: **Not started** · **Interface only** · **Mock** · **Partial** ·
**Complete** · **Blocked (SDK)** · **Blocked (entitlement)** · **Experimental** ·
**Estimated** · **Tested** · **Benchmarked**.

A feature is only marked **Complete** when its source exists, is wired into the
app, the solution compiles, tests exist and pass, and limitations are documented.
A button/view/interface/mock value alone never counts as complete.

> Current milestone: **Phases 0–7 + 9 delivered; Phase 8 blocked by the Rithmic
> SDK.** The cross-platform core (23 projects) builds on Linux/CI; the WPF shell
> builds on Windows. 161 tests pass, including headless SkiaSharp pixel tests of
> the mandated candle colors, the liquidity heatmap, all eight order-flow
> detectors, the review system, and end-to-end record → DuckDB inspect →
> deterministic replay.

## Phase 0 — Repository foundation
| Feature | Status | Notes |
|---|---|---|
| Solution structure (26 projects) | Complete · Tested | Builds via `FlowTerminal.sln` (Windows) / `FlowTerminal.Core.slnf` (cross-platform). |
| Hosting + DI + Serilog | Partial | Host + Serilog file logging wired in `App.xaml.cs`. Full DI composition grows per phase. |
| Configuration | Partial | `appsettings.example.json` placeholders; binding expands in later phases. |
| Mock build profile | Complete · Tested | Builds with zero proprietary files; CI `core` job enforces it. |
| Rithmic adapter boundary | Interface only · Blocked (SDK) | `RITHMIC_SDK` compile symbol; no fabricated SDK code. |
| CI (GitHub Actions) | Complete | `core` (Linux) + `windows` jobs. |
| Documentation skeleton | Complete | All `docs/*.md` present. |
| Application shell (WPF) | Mock | Read-only shell, banners, no order entry. Builds on Windows. |

## Phase 1 — Domain & event pipeline
| Feature | Status | Notes |
|---|---|---|
| NQ/ES instrument specs | Complete · Tested | Tick 0.25; NQ $20/pt ($5/tick), ES $50/pt ($12.50/tick). |
| Integer-tick price conversion | Complete · Tested | No binary float for prices; `PriceConverter`. |
| Contract model + quarterly calendar | Complete · Tested | Symbols discovered/constructed, never hard-coded; third-Friday expiry. |
| Suggested active contract | Complete · Tested | Advisory only; user stays in control. |
| Canonical `MarketEvent` (readonly struct) | Complete · Tested | One model drives all downstream consumers. |
| Clock abstraction | Complete · Tested | `SystemClock` / `ManualClock`. |
| Session templates + DST handling | Complete · Tested | Globex/RTH/ETH/Overnight/Custom; UTC internally; DST verified. |
| Bounded event pipeline (single-writer) | Complete · Tested | Bounded channel; never drops canonical events. |
| Sequence validation & gap detection | Complete · Tested | Gap → invalidation + downstream `SequenceGap`. |
| Mock market-data provider | Complete · Tested | Deterministic synthetic NQ/ES; flagged SIMULATED. |
| Synthetic historical / reference data | Mock · Tested | Trades + bars only; honest capabilities. |
| Diagnostics counters | Complete · Tested | Feeds the (future) diagnostics overlay. |
| Throughput benchmark | Complete · Benchmarked | 500k events processed; **measured ~872k events/sec** (Debug, Linux CI). Goal ≥ 50k. |
| In-memory replay round-trip | Complete · Tested | Deterministic replay hashes match (full validator = Phase 5). |

## Phase 2 — Order book
| Feature | Status | Notes |
|---|---|---|
| Market-by-price book | Complete · Tested | Aggregated depth, best bid/ask, cumulative depth, crossed/negative detection. |
| Market-by-order book | Complete · Tested | Order-id FIFO queues; add/modify/cancel/execute/replace; queue analysis. Active only when MBO-entitled. |
| Snapshot / incremental / book clear | Complete · Tested | Invalid until snapshot completes; visible `InvalidReason`. |
| Sequence-gap invalidation + resync | Complete · Tested | Gap → invalid until fresh snapshot rebuilds. |
| Book checkpoints (replay seeking) | Complete · Tested | Restore + apply-rest == full replay (`CheckpointSeekTests`). |
| Order-book benchmark | Complete | `OrderBookBenchmark` (apply throughput + allocations). |

## Phase 3 — Bars & core analytics
| Feature | Status | Notes |
|---|---|---|
| Time / tick / volume / range bars | Complete · Tested | Exact-fixture OHLC + delta. |
| Delta & CVD | Complete · Tested | O(1) incremental; unknown aggressor neutral. |
| Volume profile (POC / VAH / VAL) | Complete · Tested | Developing = on-demand query; safe when empty. |
| Footprints + diagonal/horizontal imbalance | Complete · Tested | Documented formula; zero-denominator safe (no NaN/Inf). |
| VWAP + std-dev bands | Complete · Tested | Incremental; daily/weekly/anchored via Reset. |
| Live == replay analytics | Complete · Tested | Same code path; determinism test. |

## Phase 4 — Core UI
| Feature | Status | Notes |
|---|---|---|
| SkiaSharp chart viewport + candlestick renderer | Complete · Tested | Single-canvas; **no per-candle WPF controls**. |
| Candle colors (green / light-purple) | Complete · Tested | Headless SkiaSharp **pixel** tests assert #22C55E / #C4A7FF and not-red. |
| Read-only DOM model + Skia ladder | Complete · Tested | Observational; no order-entry fields (asserted by reflection). |
| Time & Sales (virtualized ring buffer) | Complete · Tested | Bounded memory; filters; large-trade flag; speed-of-tape. |
| CVD / book-validity readouts | Complete | Live in shell from the feed snapshot. |
| Workspace state (JSON) | Complete · Tested | Round-trips; SQLite persistence in Phase 5. |
| WPF shell wired to live mock feed | Mock · Windows-only | Pipeline off-thread; 30 FPS coalesced render. Runtime verified on Windows. |

## Phase 5 — Recording & replay
| Feature | Status | Notes |
|---|---|---|
| Parquet recorder (batched, atomic) | Complete · Tested | Ordered part files; temp+rename; never rewrites the session per event. |
| Parquet replay source | Complete · Tested | In-order; corrupt/partial part detected and skipped (`CorruptParts`). |
| SQLite repositories + migrations | Complete · Tested | Settings/workspaces/notes; `user_version` migrations; WAL; reopen-survives. |
| DuckDB inspection | Complete · Tested | Query parts via glob; counts + per-type breakdown. |
| ReplayValidator (hash compare) | Complete · Tested | Two passes, identical state hash; CLI wired. |
| DataInspector / ReplayValidator CLIs | Complete | Wired to the real engines. |
| End-to-end record→inspect→validate | Complete · Tested | Integration test. |

## Phase 6 — Heatmap & advanced DOM
| Feature | Status | Notes |
|---|---|---|
| Liquidity heatmap model | Complete · Tested | Tiled history, dirty-tile tracking, bounded ring, persistence; no per-event rebuild. |
| Heatmap scale modes | Complete · Tested | Absolute / percentile / logarithmic. |
| Heatmap renderer | Complete · Tested | Visible-range only; bid-green/ask-purple pixel test. |
| Advanced DOM pulling/stacking/replenishment | Complete · Tested | `PullStackTracker`; observational only. |
| Heatmap wired into WPF shell | Mock · Windows-only | Rendered under feed lock; repaint at 30 FPS. |

## Phase 7 — Order-flow detectors
| Feature | Status | Notes |
|---|---|---|
| Large trade | Complete · Tested | Fixed + rolling-percentile; NQ/ES defaults. |
| Sweep | Complete · Tested | Consecutive levels, max gap, min volume/levels, direction. |
| Absorption | Complete · Tested | Volume absorbed in a tight band over a window. |
| Replenishment | Complete · Tested | Repeated deplete/restore per price within window. |
| Iceberg | Complete · Tested · **Estimated** | Trades+MBP estimate with confidence; never claimed certain. |
| Stop-run | Complete · Tested | Swing break + volume burst; classification left to user. |
| Market regime | Complete · Tested | Quiet/Normal/Fast/Extreme from rolling activity. |
| Delta divergence | Complete · Tested | Higher-high/weaker-delta and inverse, on bars. |
| Detector engine + determinism | Complete · Tested | Toggles, tooltips, measurements; replay-safe; wired into shell. |

## Phase 9 — Notes & session review
| Feature | Status | Notes |
|---|---|---|
| Timestamped notes (SQLite) | Complete · Tested | Tags; query by root/contract/tag/date range. |
| Bookmarks → replay timestamp | Complete · Tested | Jump-to-moment data model + storage. |
| Manual annotations | Complete · Tested | Entry/exit/stop/target/outcome; always flagged manual-unverified; RR computed. |
| Screenshot store | Complete · Tested | Saves chart PNGs with sortable names. |
| Export (CSV / JSON) | Complete · Tested | Notes/bookmarks/annotations; CSV escaping; manual flag. |
| Persistence wired into shell | Complete | SQLite + repos + screenshot store registered in DI. |
| Notes/review WPF panel | Not started | Data layer complete; entry/filter/jump UI is remaining UI work. |

## Phases 8 & 10 — Remaining
| Phase | Status |
|---|---|
| 8 Rithmic live adapter | **Blocked (SDK)** — boundary in place; needs authorized R\|API+ SDK. |
| 10 Packaging & stabilization | Not started |

## Known limitations (current)
- The WPF shell and `UiTests` requiring WPF build/run on **Windows only**; the
  Linux CI job covers the cross-platform core and all non-WPF tests.
- Live Rithmic data is unavailable until the authorized SDK is installed and wired
  (Phase 8). All Rithmic providers fail loudly rather than fabricate data.
- Charts, DOM, footprints, heatmap, and detectors are **not yet implemented**;
  the shell shows labelled placeholders for those regions.
