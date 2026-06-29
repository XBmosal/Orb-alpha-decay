# Indicators

Flow Terminal indicators are strictly **observational**. They identify and highlight
conditions; they never place, modify, simulate, or suggest an order.

Two families exist today:

1. **Order-flow / profile studies** — CVD, VWAP, Volume Profile, TPO, Footprint,
   Imbalance, Large-Trade, Iceberg/Absorption, Stop-Run, Speed-of-Tape, FVG, ORB, ADR,
   Chaikin A/D, Gap. These are catalogued in `Charting/Studies/StudyCatalog.cs` and
   wired into the chart overlays. See `docs/DETECTORS.md` for the order-flow detectors.
2. **Standard technical library** — the price/volume indicators documented below,
   implemented in `FlowTerminal.Analytics/Indicators/` as deterministic, incremental
   calculators. They are calculation-complete and unit-tested; chart-pane rendering and
   the per-indicator settings UI are the next integration phase (tracked in
   `docs/INDICATOR_STATUS.md`).

## Conventions

- **Prices are integer ticks** expressed as `double` for the math; conversion to
  decimal prices happens only at the display boundary.
- **Price source** (`PriceSource`): `Close` (default), `Open`, `High`, `Low`,
  `Hl2`=(H+L)/2, `Hlc3`/`Typical`=(H+L+C)/3, `Ohlc4`=(O+H+L+C)/4,
  `WeightedClose`=(H+L+2C)/4.
- **Warm-up**: every indicator returns `NaN` and reports `IsReady == false` until it
  has seen enough inputs. No value is ever emitted from insufficient data.
- **Determinism**: indicators are pure functions of their input stream and settings;
  live, mock, recorded, and replay data produce identical outputs.
- **No look-ahead**: only the current and prior confirmed inputs are used. Values on
  the developing bar may change until the bar closes; confirmed bars never repaint.
- **NaN/∞ safety**: invalid inputs are ignored; all divisions are guarded.
- **EMA/RMA seeding**: exponential and Wilder averages are seeded with the simple
  average of the first `period` inputs (the classic convention), so results are
  reproducible.

## Trend

### Moving Average (`ma`)
- **Formula**: SMA = mean of last *n*; EMA α=2/(n+1); RMA (Wilder/SMMA) α=1/n;
  WMA weights 1..n (recent highest); HMA = WMA√n(2·WMA(n/2) − WMA(n)).
- **Inputs**: price source, period, type. **Output**: one line (ticks).
- **Warm-up**: *n* inputs (HMA a few more). **Repaints**: no. **Overlay**: yes.
- **NQ/ES defaults**: EMA, period 20, Close.

### MACD (`macd`)
- **Formula**: MACD = EMA(fast) − EMA(slow); Signal = EMA(signal) of MACD;
  Histogram = MACD − Signal.
- **Defaults**: 12 / 26 / 9, Close. **Warm-up**: ≈ slow+signal. **Pane**: separate.

### Average Directional Index (`adx`)
- **Formula**: Wilder +DM/−DM and TR smoothed with RMA; +DI=100·RMA(+DM)/ATR,
  −DI=100·RMA(−DM)/ATR; DX=100·|+DI−−DI|/(+DI+−DI); ADX = RMA(DX). Bounded 0–100.
- **Defaults**: period 14. **Warm-up**: ≈ 2·period. **Pane**: separate.

## Volatility

### Average True Range (`atr`)
- **Formula**: TR = max(H−L, |H−Cₚ|, |L−Cₚ|); ATR = RMA(TR). Output in ticks.
- **Defaults**: period 14. **Warm-up**: period. **Pane**: separate.

### Bollinger Bands (`bbands`)
- **Formula**: middle = SMA(n); upper/lower = middle ± k·σ (population stddev, divisor
  N); bandwidth = (upper−lower)/middle.
- **Defaults**: 20, k=2, Close. **Overlay**: yes. **Warm-up**: period.

### Donchian Channel (`donchian`)
- **Formula**: upper = highest high(n), lower = lowest low(n), middle = (upper+lower)/2.
- **Defaults**: 20. **Overlay**: yes. **Warm-up**: period.

### Keltner Channel (`keltner`)
- **Formula**: middle = EMA(close, n); upper/lower = middle ± mult·ATR(atrN).
- **Defaults**: EMA 20, ATR 10, mult 2. **Overlay**: yes.

## Momentum / Oscillators

### RSI (`rsi`)
- **Formula**: avgGain/avgLoss via Wilder RMA; RSI = 100 − 100/(1 + avgGain/avgLoss),
  clamped 0–100; RSI = 100 when avgLoss = 0.
- **Defaults**: period 14, Close. **Warm-up**: period+1. **Pane**: separate (0–100).

### Stochastic Oscillator (`stoch`)
- **Formula**: rawK = 100·(C − lowₙ)/(highₙ − lowₙ) (=50 when the range is flat);
  %K = SMA(kSmooth) of rawK; %D = SMA(dPeriod) of %K. Bounded 0–100.
- **Defaults**: 14 / 3 / 3. **Pane**: separate.

### Commodity Channel Index (`cci`)
- **Formula**: (TP − SMA(TP))/(0.015·meanAbsDev(TP)), TP=(H+L+C)/3; 0 when meanDev=0.
- **Defaults**: period 20. **Pane**: separate.

### Rate of Change (`roc`) and Momentum (`momentum`)
- **ROC**: 100·(price − priceₙ)/priceₙ (0 when the lagged price is 0).
- **Momentum**: price − priceₙ.
- **Defaults**: ROC 9, Momentum 10, Close. **Pane**: separate.

## Notes on interpretation

These indicators describe the recent price/volume record. They are not predictions and
carry no profit claim. For example, an ADX above ~25 is conventionally read as a
trending regime and a low ADX as range-bound — but that is context for the observer,
not a signal to act, and Flow Terminal never acts on it.
