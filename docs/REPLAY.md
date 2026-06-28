# Recording & Replay

**Status: Interface only (Phase 5).** `IReplaySource` and an `InMemoryReplaySource`
exist now and the Phase-1 integration test already proves deterministic round-trip
replay (replay hashes match the recording, twice). Parquet-backed recording,
checkpoint seeking, and the full `ReplayValidator` land in Phase 5.

## Determinism
Replay uses the **same** event model, book engine, bar engine, analytics, detector
engine, and chart models as live data. It never uses future information.

## Planned controls (Phase 5)
Select NQ/ES, contract, date, session, start/end; play, pause, stop, restart; step
event / ms / second; jump to time; speeds 0.25×…400× + max + custom.

## ReplayValidator (Phase 5)
Replays a session, hashes final bars + footprints + CVD + profiles + order-book
state, replays again, compares hashes, and produces a difference report.
Checkpoints (book/bars/CVD/profile/session stats/detector state) enable efficient
seeking.
