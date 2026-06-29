# Indicator status

Legend: ✅ done · 🟡 partial · ⬜ not started · n/a not applicable.

"Rendering" = drawn on the chart/pane. "Settings UI" = per-instance configurable
controls. "Persistence" = restored from workspace/template. The standard technical
library below is **calculation-complete and unit-tested**; chart rendering and the
per-indicator settings UI are the next integration phase.

## Standard technical library (`FlowTerminal.Analytics/Indicators`)

| Indicator | Calc | Tested | Live/Replay parity | Rendering | Settings UI | Persistence | Data | Notes |
|---|---|---|---|---|---|---|---|---|
| Moving Average (SMA/EMA/WMA/RMA/HMA) | ✅ | ✅ | ✅ (deterministic) | ⬜ | ⬜ | ⬜ | OHLC | reference vectors |
| MACD | ✅ | ✅ | ✅ | ⬜ | ⬜ | ⬜ | OHLC | 12/26/9 |
| ADX (+DI/−DI) | ✅ | ✅ | ✅ | ⬜ | ⬜ | ⬜ | OHLC | Wilder |
| ATR | ✅ | ✅ | ✅ | ⬜ | ⬜ | ⬜ | OHLC | ticks |
| Bollinger Bands | ✅ | ✅ | ✅ | ⬜ | ⬜ | ⬜ | OHLC | population σ |
| Donchian Channel | ✅ | ✅ | ✅ | ⬜ | ⬜ | ⬜ | OHLC | |
| Keltner Channel | ✅ | ✅ | ✅ | ⬜ | ⬜ | ⬜ | OHLC | EMA+ATR |
| RSI | ✅ | ✅ | ✅ | ⬜ | ⬜ | ⬜ | OHLC | Wilder, 0–100 |
| Stochastic | ✅ | ✅ | ✅ | ⬜ | ⬜ | ⬜ | OHLC | %K/%D |
| CCI | ✅ | ✅ | ✅ | ⬜ | ⬜ | ⬜ | OHLC | |
| Rate of Change | ✅ | ✅ | ✅ | ⬜ | ⬜ | ⬜ | OHLC | |
| Momentum | ✅ | ✅ | ✅ | ⬜ | ⬜ | ⬜ | OHLC | |

### Not yet started (standard library)
SuperTrend, Parabolic SAR, Ichimoku, Linear Regression / Regression Channel, Tillson
T3, Zig Zag, Williams %R, Awesome Oscillator, Aroon, Know Sure Thing, Inverse Cyber
Cycle, Pivot Points. (Architecture supports them; they are future increments.)

## Order-flow / profile studies (existing, `Charting/Studies/StudyCatalog.cs`)

| Study | Calc | Rendering | Settings UI | Data | Notes |
|---|---|---|---|---|---|
| Volume Profile (VBP) | ✅ | ✅ | 🟡 (on/off) | PriceLevelVolume | active |
| Volume (per bar) | ✅ | ✅ | 🟡 | Volume | active |
| Bid/Ask Cluster Profile | ✅ | 🟡 | 🟡 | AggressorSide | engine-ready |
| Delta Profile | ✅ | 🟡 | 🟡 | AggressorSide | engine-ready |
| TPO / Market Profile | ✅ | 🟡 | 🟡 | Session | engine-ready |
| VWAP (+bands, anchored) | ✅ | ✅ | 🟡 | Volume | active |
| Footprint / Cluster | ✅ | ✅ | 🟡 | AggressorSide | engine-ready |
| Volume Imbalance Tracker | ✅ | 🟡 | 🟡 | AggressorSide | engine-ready |
| CVD | ✅ | ✅ | 🟡 | AggressorSide | active |
| Large / Block Trade | ✅ | ✅ | 🟡 | AggressorSide | detector |
| Iceberg & Absorption | ✅ | ✅ | 🟡 | MBP (Estimated) | detector; MBO would upgrade |
| Speed of Tape | ✅ | 🟡 | 🟡 | Trades+Timestamps | engine-ready |
| Stop Run / Liquidity Hunt | ✅ | ✅ | 🟡 | AggressorSide | detector |
| Fair Value Gap | ✅ | 🟡 | 🟡 | OHLC | engine-ready |
| Opening Range Breakout | ✅ | 🟡 | 🟡 | Session | engine-ready |
| Average Daily Range | ✅ | 🟡 | 🟡 | OHLC+Session | engine-ready |
| Chaikin A/D | ✅ | 🟡 | 🟡 | Volume | engine-ready |
| Gap Detector | ✅ | 🟡 | 🟡 | OHLC+Session | engine-ready |
| Multi-Timeframe Overlay | ⬜ | ⬜ | ⬜ | OHLC | planned |
| Volume Profile on Swing | ⬜ | ⬜ | ⬜ | PriceLevelVolume+swings | planned (needs swing detector) |

## Capability-aware (MBO) items — architecture present, data-limited

Mock/Rithmic MBP feeds do not carry order-level (MBO) detail. Iceberg/Absorption and
passive-wall studies therefore run in **Estimated** mode and are labelled as such; an
MBO entitlement would enable exact modes. This is documented, not faked.

## Cross-cutting guarantees (standard library)

- Warm-up enforced (NaN until ready). · No NaN/∞ leaks (guarded divisions, invalid
  inputs ignored). · Incremental == independent batch (tested). · Reset reproduces the
  sequence (tested). · Bounded memory (ring buffers, no full-history recompute).
