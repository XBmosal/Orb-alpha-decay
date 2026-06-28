# Order Book

**Status: Interface only (Phase 2).** `FlowTerminal.OrderBook.IOrderBook` fixes the
read-only book contract; the MBP and MBO engines are implemented in Phase 2.

The book is strictly **observational**. Because the application never submits
orders, there is no concept of the user's own orders or queue position.

## Planned behavior (Phase 2)
- **Market-by-price:** aggregate size per price; sorted bids/asks; best bid/ask;
  add/change/remove tracking; cumulative depth; invalid-book detection.
- **Market-by-order (when entitled):** orders by id; price-level queues;
  reductions/cancels/replaces/executions; priority ordering; replenishment; queue
  analysis.
- **Lifecycle:** initial snapshot, incremental updates, snapshot replacement,
  sequence-gap invalidation, book clear, reconnect, resubscribe, resync, session
  reset, contract change.
- **Tests (Phase 2):** add, partial/full cancel, partial/full execution, modify,
  replace, duplicate, out-of-order, sequence gap, snapshot reconstruction, book
  clear, reconnect, aggregation, best bid/ask, crossed book, negative-quantity
  prevention, unknown order id.
- Periodic **book checkpoints** for efficient replay seeking.

A detected sequence gap already arrives as a canonical `SequenceGap` event from the
pipeline (Phase 1), which the engine will use to invalidate and resynchronize.
