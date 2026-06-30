# Order-Flow Detectors

**Status: Complete · Tested** (Phase 7). All eight detectors are implemented,
documented, unit-tested, replay-deterministic, individually toggleable, and carry a
tooltip plus supporting measurements. **No detector claims guaranteed institutional
intent**; heuristics inferred without direct exchange evidence are flagged
`IsEstimated` and labelled "(Estimated)".

Each detector emits a `Detection { DetectorName, TimestampUtc, PriceTicks, Bias,
IsEstimated, Description, Measurements }`. The `DetectorEngine` runs the full suite
over the canonical stream (live and replay share it) and keeps a bounded ring of
recent detections.

## Large Trade (`LargeTradeDetector`)
Flags outsized prints. A trade qualifies when it meets a **fixed threshold** and,
if enabled, also exceeds a **rolling percentile** (default 98th) of recent trade
sizes — i.e. session-adaptive. Separate NQ/ES defaults (`ForNq`/`ForEs`).
Measurements: `size`, `threshold`.

## Sweep (`SweepDetector`)
A same-direction run of aggressive trades that takes ≥ `MinLevels` consecutive
price levels within `MaxGap` between prints and ≥ `MinVolume` total. A direction
change, a time gap, or a price reversal ends the run. Measurements: `levels`,
`volume`, `ticksSpanned`, `durationMs`.

## Absorption (`AbsorptionDetector`)
≥ `MinVolume` aggressive volume executes within a `MaxMoveTicks` price band over
`Window` without price moving away — resting liquidity absorbs the aggression. The
bias is the **resting** side (opposite the dominant aggressor). Measurements:
`totalVolume`, `buyVolume`, `sellVolume`, `moveTicks`.

## Replenishment (`ReplenishmentDetector`)
A price level repeatedly depleted and restored (≥ `MinReplenishments` times, each
restore ≥ `MinSize`) within `Window`. Per-price counters. Measurements: `size`.

## Iceberg (`IcebergDetector`) — Estimated
With only trades + market-by-price this can never be certain, so **every result is
`IsEstimated = true`** with a confidence figure. It flags a price where executed
volume ≥ `RefillRatio` × the largest displayed size (≥ `MinExecuted`), consistent
with a hidden refilling order. (A stronger, non-estimated detector applies when MBO
is entitled.) Measurements: `executed`, `maxDisplayed`, `ratio`, `confidence`.

## Stop-Run (`StopRunDetector`)
Price breaks a recent swing high/low (over `Lookback`) by `BreakoutBufferTicks`
accompanied by a short-window **volume burst** ≥ `VolumeBurstFactor` × the
lookback's average burst (the breaking print's own size counts). Continuation vs
rejection requires follow-through and is left to the user. Measurements: `swing`,
`breakTicks`, `shortVolume`, `avgBurst`.

## Market Regime (`MarketRegimeDetector`)
Classifies **Quiet / Normal / Fast / Extreme** from a rolling window of trade rate,
book-update rate, realized volatility, and spread. Emits only on a regime change.
Descriptive, not predictive. Measurements: `tradesPerSecond`,
`bookUpdatesPerSecond`, `realizedVolTicks`, `spreadTicks`.

## Delta Divergence (`DeltaDivergenceDetector`)
On completed bars: **bearish** when price makes a higher high than the lookback
swing but bar delta is weaker; **bullish** on a lower low with stronger delta.
Configurable `Lookback`. No predictive guarantee. Measurements: `curDelta`,
`swingDelta`.

## Tests
Per-detector unit tests with hand-built fixtures (thresholds, direction, time gaps,
zero/edge cases, the disabled toggle, NQ vs ES defaults, and the estimated label),
plus a determinism test that runs the whole suite over a synthetic session twice
and asserts identical detection counts (replay-safe).
