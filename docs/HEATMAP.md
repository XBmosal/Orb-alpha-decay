# Liquidity Heatmap & Advanced DOM

**Status: Model + renderer Complete · Tested** (Phase 6). The heatmap history model,
renderer, and the advanced-DOM pulling/stacking/replenishment tracker are
implemented and tested; the heatmap is wired into the WPF shell.

## Liquidity heatmap (`LiquidityHeatmap`)
Time×price history of **displayed** liquidity (bid green, ask purple), built for the
performance constraints in the spec:

- **No per-event rebuild.** A new time *column* is sealed only when the column
  interval elapses (default 250 ms); depth updates mutate the *current* column in
  place. Liquidity **persists** forward into new columns until it changes.
- **Tiled + dirty tracking.** Columns are grouped into fixed-size tiles; any change
  marks that tile dirty (`DirtyTiles`/`ClearDirty`) so a renderer rebuilds only
  dirty tiles, never the whole session.
- **Bounded memory.** A ring of at most `maxColumns` columns evicts the oldest, so
  memory stays bounded across a long session. Raw events live in storage; this is
  only the visual cache.
- **Scale modes.** Absolute, percentile (95th by default), and logarithmic
  intensity normalization over the visible range.

`HeatmapRenderer` draws only the visible columns/prices onto one Skia canvas (no
per-cell controls). A headless pixel test confirms bid renders green below mid and
ask renders light-purple above.

## Advanced DOM (`PullStackTracker`)
Per price level, observationally tracks displayed-size dynamics:
- **Pulling** — cumulative size removed.
- **Stacking** — cumulative size added (the first sighting of a level is a baseline,
  not stacking).
- **Replenishment** — count of times a level was restored after being depleted.

It measures changes in displayed size only and makes no claim about intent.

## Tests (8)
Heatmap: in-column updates don't seal per event; clock seals + persists; dirty
tiles marked/cleared; memory bounded by eviction; scale-mode intensity. Render:
bid-green/ask-purple pixel test. PullStack: pulling/stacking totals; replenishment
counting.

## Planned
Percentile/log UI toggles, executed-trade bubble overlay, volume-profile/CVD
overlays, and tile texture caching on the GPU path are incremental additions on
this model.
