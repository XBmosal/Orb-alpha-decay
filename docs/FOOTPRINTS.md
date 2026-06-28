# Footprints

**Status: Not started (Phase 3).** All footprint math will be computed from the
canonical event stream — identical code for live and replay.

## Planned metrics
Bid/ask/total volume at price; price-level and bar delta; bar/session cumulative
delta; max +/- delta; bar volume; trade count; POC; VAH/VAL; developing POC/value
area; diagonal & horizontal imbalances (configurable ratio/min volume); stacked &
extended imbalance zones; zero prints; unfinished-auction markers; large-trade
highlighting; volume clusters; delta divergence.

## Diagonal imbalance definition
Compare **ask** volume at a price against **bid** volume one tick **below**, and
**bid** volume at a price against **ask** volume one tick **above**. Zero
denominators are handled safely — never produce Infinity, NaN, or invalid ratios.
Every formula will be documented here with worked examples and unit tests.
