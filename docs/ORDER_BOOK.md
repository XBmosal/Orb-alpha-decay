# Order Book

**Status: Market-by-price Complete · Tested; Market-by-order Complete · Tested
(MBO only active when the feed is entitled).** Implemented in Phase 2.

The book is strictly **observational**. Because the application never submits
orders, there is no concept of the user's own orders or queue position.

## Market-by-price (`MarketByPriceOrderBook`)
Aggregated size per price level, best-first ordering, cached best bid/ask
(recomputed only when the best level is removed — no LINQ on the hot path).

- Depth comes from `BidUpdate` / `AskUpdate` (new aggregated size at a price; size
  0 removes the level). Trades do **not** mutate displayed depth in MBP mode.
- `CumulativeDepth(side, levels)` for the DOM cumulative columns.
- Integrity: invalid until a snapshot completes; **crossed book** (best bid ≥ best
  ask) flagged invalid; **negative sizes rejected**; `SequenceGap` invalidates and
  the book stays invalid (visibly, via `InvalidReason`) until a fresh snapshot
  rebuilds it; `BookClear` empties and invalidates.

## Market-by-order (`MarketByOrderBook`)
Individual orders by id within **FIFO price-level queues**, enabling queue analysis
and replenishment tracking when the feed is MBO-entitled.

- `Add` (new resting order), `Modify` (size change in place — priority preserved;
  price change = **replace**, re-added at the tail with priority reset),
  `Cancel` (remove), `Execute` (size reduction; removes at zero).
- `QueueAt(side, price)` exposes FIFO order/size pairs.
- **Unknown order ids** and **duplicate adds** are counted (`UnknownOrderCount`,
  `DuplicateAddCount`) rather than corrupting the book.

## Checkpoints
`CreateCheckpoint()` / `RestoreCheckpoint()` capture and restore full book state.
`CheckpointSeekTests` proves restoring a checkpoint and applying only subsequent
events yields a state **identical** to full replay — the basis for efficient
replay seeking.

## Lifecycle handled
Initial snapshot, incremental updates, snapshot replacement, sequence-gap
invalidation, book clear, and resynchronization. Reconnect/resubscribe/contract-
change reuse `Clear()` + a fresh snapshot.

## Tests (23, all passing)
MBP: snapshot reconstruction, incremental updates, level removal + best recompute,
aggregation, cumulative depth, crossed book, negative-size prevention, sequence-gap
invalidation + resync, book clear, checkpoint round-trip, determinism.
MBO: add, partial cancel (modify), full cancel, partial/full execution, modify,
replace (price move + priority reset), duplicate add, unknown order id, FIFO
priority, empty-level removal, sequence-gap invalidation.
Plus the checkpoint-seek determinism integration test.
