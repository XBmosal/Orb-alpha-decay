# Order-Flow Detectors

**Status: Not started (Phase 7).** Each detector will be transparent,
configurable, documented, unit- and replay-tested, with an enable/disable toggle,
tooltip, and supporting measurements. No detector claims guaranteed institutional
intent.

## Planned detectors
- **Large trade** — fixed / rolling-percentile / session-adaptive; separate NQ/ES
  defaults.
- **Sweep** — consecutive levels, max time gap, min total volume, min levels,
  direction.
- **Absorption** — repeated aggression with limited price movement; min volume;
  time window; optional replenishment.
- **Liquidity replenishment** — repeated consumption/restoration; per-price
  counter; quantity/time thresholds.
- **Iceberg (Estimated)** — prefers MBO evidence; MBP/trade estimation labelled
  Estimated with confidence/measurements; never claims certainty.
- **Stop-run** — movement through recent swings; increased aggression and tape
  speed; rejection/continuation classification.
- **Market regime** — trade rate, book-update rate, realized volatility, spread,
  near-market depth → Quiet/Normal/Fast/Extreme.
- **Delta divergence** — higher high with weaker delta / lower low with stronger
  delta; configurable lookback; no predictive guarantee.
