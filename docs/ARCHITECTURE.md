# Architecture

Flow Terminal is a **modular monolith**: one native Windows desktop application
(WPF + SkiaSharp) composed of cleanly separated .NET 8 libraries. It is strictly
a **read-only market-data visualization and analysis** platform for NQ and ES
futures. There is **no execution surface** of any kind — no order submit/modify/
cancel, no accounts, positions, P&L, risk, fills, or trade copying.

## Design principles (priority order)
1. Data integrity over everything visual.
2. Correct event sequencing and order-book reconstruction.
3. Deterministic replay (live and replay share identical analytics code).
4. Correct footprint / delta / profile / session math.
5. Stability, then UI responsiveness, then CPU/memory efficiency.
6. Visual updates may be batched, coalesced, rate-limited, downsampled, or skipped.
   **Canonical market events are never silently dropped.**

## Layering
```
FlowTerminal.App            WPF shell (Windows-only). Composition root, rendering host.
  └─ FlowTerminal.Charting  SkiaSharp rendering primitives + color palette.
  └─ FlowTerminal.Analytics Bars, footprints, delta, CVD, profiles, VWAP, detectors.
  └─ FlowTerminal.OrderBook Read-only MBP/MBO reconstruction.
  └─ FlowTerminal.Replay    Deterministic playback of recorded events.
  └─ FlowTerminal.Storage   SQLite (metadata) + Parquet/DuckDB (events) [Phase 5].
  └─ FlowTerminal.Notes     Session-review notes/bookmarks.
  └─ FlowTerminal.MarketData Provider abstractions + bounded pipeline + mock/synthetic.
       └─ FlowTerminal.Rithmic  Authorized live adapter (behind RITHMIC_SDK).
  └─ FlowTerminal.Infrastructure  Hosting, paths, run-mode, product identity.
  └─ FlowTerminal.Domain    Instruments, ticks, contracts, sessions, canonical events, clock.
```
Dependencies point inward toward `Domain`. External dependencies sit behind
interfaces (`IMarketDataProvider`, `IHistoricalDataProvider`,
`IReferenceDataProvider`, `IMarketDataRecorder`, `IReplaySource`, `IOrderBook`,
`IClock`, `ISettingsRepository`, `IWorkspaceRepository`, `INotesRepository`).

## Event pipeline
```
Provider → Normalizer → Sequence validator → Recorder → Instrument processor
        → Order-book engine → Bars/analytics → UI snapshot publisher
```
- One **deterministic single-writer processor per instrument**
  (`InstrumentPipeline`). NQ and ES process independently; no global lock.
- A **bounded** `System.Threading.Channels` queue applies backpressure
  (`BoundedChannelFullMode.Wait`). Near capacity → shed optional visual work, warn
  the user — never drop canonical events.
- A detected sequence gap emits a canonical `SequenceGap` event downstream so the
  order book can invalidate and resynchronize from a snapshot.

## Run modes
- **Mock** — deterministic synthetic NQ/ES; always available; used by CI/tests/demos.
- **Replay** — deterministic playback of recorded sessions.
- **Rithmic** — authorized live data; requires the SDK build profile (Phase 8).

The same downstream analytics run in every mode; only the source differs.

## Build profiles
- **Mock profile (default):** builds with zero proprietary files. CI enforces this.
- **Rithmic profile:** `dotnet build -p:EnableRithmic=true` defines `RITHMIC_SDK`
  and compiles the adapter against the installed authorized SDK. See
  `RITHMIC_SETUP.md` and `lib/rithmic/README.md`.

## Cross-platform note
Only `FlowTerminal.App` (and WPF-dependent UI tests) require Windows. Every other
project targets `net8.0` and builds/tests on Linux, which is what the CI `core`
job and `FlowTerminal.Core.slnf` cover.
