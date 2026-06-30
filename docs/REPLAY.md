# Recording & Replay

**Status: Complete · Tested** (Phase 5). Recording, replay, the ReplayValidator,
crash-safe writing, and DuckDB inspection are implemented and tested end-to-end on
the synthetic feed. Checkpoint-based seeking exists for the order book (Phase 2);
full UI scrub controls are part of the UI work.

## Storage layout
```
data/venue=CME/root=<NQ|ES>/contract=<symbol>/date=<yyyy-MM-dd>/events-NNNN.parquet
```
The canonical event stream is recorded as ordered Parquet **part files**. Appending
a new part (instead of rewriting one growing file) keeps writes cheap and crash
safe. Trades/depth/bars are derived projections produced on demand by DuckDB — the
ordered event log is the single source of replay truth, which keeps replay
byte-exact.

## Recorder (`ParquetMarketDataRecorder`)
Buffers events and flushes in batches. Each flush writes a temporary file that is
**atomically renamed** into place, so a crash leaves at most one incomplete `.tmp`
file and never corrupts a committed part. A session file is never rewritten per
event.

## Replay source (`ParquetReplaySource`)
Reads part files in order and yields canonical events. A truncated/corrupt part
(e.g. from an interrupted write) is **detected and skipped** rather than aborting
the replay, and counted in `CorruptParts`. Replay never uses future information and
implements the same `IReplaySource` interface as the in-memory source.

## Determinism — `ReplayValidator`
Replays a session through the **same** book + bar + CVD + profile + VWAP engines as
live, twice, and compares a hash of the final reconstructed state
(`ReplayStateHash`). Identical hashes prove determinism. The
`tools/FlowTerminal.ReplayValidator` CLI wraps this (exit 0 = deterministic,
1 = mismatch).

## Inspection — DuckDB
`DuckDbInspector` (and the `tools/FlowTerminal.DataInspector` CLI) query the Parquet
parts directly via a glob — no full-session load into managed memory — for event
counts and per-type breakdowns. Exports and richer session analysis build on this.

## Metadata — SQLite
`SqliteDatabase` owns settings, workspaces, and notes with forward-only migrations
keyed off `PRAGMA user_version` and WAL mode for crash safety. Data survives a
restart (verified).

## Tests
- SQLite: schema version + idempotent migrate, settings/workspace/notes round-trip,
  query-by-root/tag, reopen/crash-recovery (8 tests).
- Parquet: record→replay reproduces events in order across multiple parts,
  time-filtered replay, corrupt-part recovery (`CorruptParts`).
- ReplayValidator: in-memory and Parquet-recorded sessions replay deterministically.
- End-to-end: record → DuckDB inspect → validate.

## Planned (UI)
Full scrub controls (play/pause/step/jump, speeds 0.25×…400×) and checkpoint-based
UI seeking are part of the ongoing UI work; the deterministic engine and book
checkpoints they build on are done.
