# Feature Status

Status legend: **Not started** · **Interface only** · **Mock** · **Partial** ·
**Complete** · **Blocked (SDK)** · **Blocked (entitlement)** · **Experimental** ·
**Estimated** · **Tested** · **Benchmarked**.

A feature is only marked **Complete** when its source exists, is wired into the
app, the solution compiles, tests exist and pass, and limitations are documented.
A button/view/interface/mock value alone never counts as complete.

> Current milestone: **Phase 0 + Phase 1 + Phase 2 delivered.** The cross-platform
> core (23 projects) builds on Linux/CI; the WPF shell builds on Windows.

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

## Phases 3–10 — Not started / scaffolded
| Phase | Status |
|---|---|
| 3 Bars & analytics (footprints, delta, CVD, profile, VWAP) | Not started |
| 4 Core UI (Skia charts, DOM, T&S) | Not started (shell + palette done) |
| 5 Recording & replay (Parquet/SQLite/DuckDB, ReplayValidator) | Interface only |
| 6 Heatmap & advanced DOM | Not started |
| 7 Detectors | Not started |
| 8 Rithmic live adapter | Blocked (SDK) |
| 9 Notes & review | Interface only (`INotesRepository`) |
| 10 Packaging & stabilization | Not started |

## Known limitations (current)
- The WPF shell and `UiTests` requiring WPF build/run on **Windows only**; the
  Linux CI job covers the cross-platform core and all non-WPF tests.
- Live Rithmic data is unavailable until the authorized SDK is installed and wired
  (Phase 8). All Rithmic providers fail loudly rather than fabricate data.
- Charts, DOM, footprints, heatmap, and detectors are **not yet implemented**;
  the shell shows labelled placeholders for those regions.
