# Indicator system audit

Audit performed before any code changes, per the indicator-session mandate. It records
what the indicator system actually was, classifies each part, and sets the work order.

## System as found

The indicator system was **metadata + on/off toggles**, not an architecture:

- `Charting/Studies/StudyCatalog.cs` — a static list of 21 studies with honest
  `Active` / `EngineReady` / `Planned` statuses and short-codes.
- `App/StudyState.cs` — a `HashSet<string>` of enabled short-codes (UI thread only).
- `App/MainWindow.xaml.cs` `BuildIndicatorsMenu` — a popup of clickable rows; no
  per-indicator settings, search, categories badges, or favorites.
- `Charting/Overlays/ChartOverlayRenderer.cs` — renders overlays keyed by short-code
  (`VBP/BAC/DP/ORB/FVG/VWAP/TPO`); `SkiaChartHost` additionally handles `FP`, `VOL`.
- `Analytics/*` — real calculators: `CvdCalculator`, `MultiVwap`, `VolumeProfile`,
  `TpoProfile`, `Footprint`, `FairValueGap`, `SessionLevels`, `ChaikinAccumulationDistribution`,
  and a `DetectorEngine` (Large/Sweep/Absorption/Replenishment/Iceberg/StopRun/Regime/Divergence).
- Persistence: `Templates.cs` stores `string[] Indicators` (enabled codes) +
  timeframe/chartType/cvdView. No per-indicator settings exist to persist.
  `WorkspaceState` stores panels, not indicators.

## Audit matrix (condensed)

| Area | Existing | Calc | Render | Settings | Persistence | Replay | Defects / gap | Action |
|---|---|---|---|---|---|---|---|---|
| Indicator architecture (IIndicator/descriptor/registry/data-reqs) | ❌ | — | — | — | — | — | none; string short-codes only | **add** (this session) |
| Standard technical library (MA/RSI/MACD/ATR/ADX/Bollinger/Keltner/Donchian/Stoch/CCI/ROC/Momentum/…) | ❌ | — | — | — | — | — | entirely missing | **add core, tested** (this session) |
| Per-indicator settings + validation UI | ❌ | — | — | ❌ | ❌ | — | only on/off | architecture added; UI next phase |
| CVD | ✅ | ✅ | ✅ | 🟡 | code only | ✅ | settings thin | keep |
| VWAP (+bands/anchored) | ✅ | ✅ | ✅ | 🟡 | code only | ✅ | settings thin | keep |
| Volume Profile / TPO / Delta / Bid-Ask | ✅ | ✅ | 🟡 | 🟡 | code only | ✅ | some EngineReady not drawn | keep |
| Footprint | ✅ | ✅ | ✅ | 🟡 | code only | ✅ | settings thin | keep |
| Detectors (Large/Iceberg/Absorption/StopRun/Sweep/Regime/Divergence/Replenish) | ✅ | ✅ | 🟡 | 🟡 | code only | ✅ | MBP→Estimated labelling ok | keep |
| FVG / ORB / ADR / Gap / Chaikin | ✅ | ✅ | 🟡 | 🟡 | code only | ✅ | overlay wiring partial | keep |
| Capability declarations (data reqs / Estimated) | 🟡 | — | — | — | — | — | detectors only, not machine-readable | **add** (IndicatorData flags) |
| Docs (INDICATORS / INDICATOR_STATUS) | ❌ | — | — | — | — | — | missing | **add** (this session) |

## Classification

- **Complete & verified**: CVD, VWAP, Volume Profile, Footprint, Large-Trade.
- **Present, render/settings unfinished**: Bid/Ask & Delta profiles, TPO, Imbalance,
  Speed-of-Tape, FVG, ORB, ADR, Chaikin, Gap (StudyStatus = EngineReady).
- **Present, data-limited (Estimated)**: Iceberg, Absorption, Replenishment, walls —
  MBP feed only; MBO would enable exact modes.
- **Planned**: Multi-Timeframe overlay, Volume-Profile-on-Swing.
- **Missing entirely**: the whole standard technical/momentum/volatility library.

## Work order chosen for this session

Per the spec ("audit first, repair existing second, then implement missing items
category by category; keep compiling; do not mass-generate untested files"), this
session delivers the highest-value, lowest-risk slice:

1. **Phase A** — a clean indicator architecture: `IIndicator`, `IBarIndicator`,
   `IScalarIndicator`, `IndicatorDescriptor`, `IndicatorCategory`, `IndicatorData`
   (capability flags), `PriceSource`, a shared `RollingWindow`, and an
   `IndicatorCatalog` registry/factory. Calculation, state, metadata and rendering are
   kept separate (no oversized base class).
2. **Phase F / §19** — the missing standard technical library implemented as
   deterministic, incremental, NaN-safe, warm-up-aware calculators with
   reference-vector and invariant tests.
3. **§29** — `docs/INDICATORS.md`, `docs/INDICATOR_STATUS.md`, and this audit.

Deferred to later increments (documented, not faked): chart-pane rendering and the
per-indicator settings UI for the new library; remaining standard indicators
(SuperTrend, SAR, Ichimoku, Regression, T3, Zig Zag, Williams %R, Aroon, AO, KST,
Pivot Points); workspace/template persistence of per-indicator settings. The existing
order-flow studies and the frozen UI/theme/heatmap were left unchanged.
