# Notes, Screenshots & Session Review

**Status: Review system Complete · Tested; WPF notes panel pending** (Phase 9). A
lightweight session-review system — **not a brokerage trade journal**. Everything
here is manually entered and is **never** represented as a broker-confirmed trade.

## Notes (`SessionNote` + `SqliteNotesRepository`)
Timestamped notes with tags, scoped to NQ/ES and a contract. Query by root, tag,
contract, and date range. Stored in SQLite (schema v2).

## Bookmarks (`Bookmark` + `SqliteBookmarkRepository`)
A bookmarked moment carries a **replay timestamp**; selecting it jumps replay to
that instant. Query by root/contract/date.

## Manual annotations (`ManualAnnotation` + `SqliteAnnotationRepository`)
Optional manual trade annotations: direction, entry/exit/stop/target (integer
ticks), outcome, and notes. `IsManualUnverified` is **always true**, and a derived
`RiskRewardRatio` is computed purely from the user's own inputs. These are review
aids, not confirmed fills, and exports mark them as manual/unverified.

## Screenshots (`ScreenshotStore`)
Saves chart PNG bytes (captured by the UI) to the screenshots directory with a
sortable, timestamped filename. No credentials are ever embedded.

## Export (`ReviewExporter`)
Notes → CSV / JSON; annotations → CSV (with the manual-unverified flag);
bookmarks → JSON. CSV escapes commas/quotes/newlines.

## Wiring
The SQLite database, repositories, and screenshot store are registered in the app
composition root (`App.xaml.cs`) and persist under
`%LOCALAPPDATA%/FlowTerminal/`. The dedicated notes/review **WPF panel** (entry
forms, jump-to-replay, filter UI) is the remaining UI work; the data layer it binds
to is complete and tested.

## Tests
SQLite round-trip + date-range filtering for notes, bookmark query by root, manual
annotation persistence + RR + unverified flag, schema upgrade to v2, CSV/JSON
export escaping, and screenshot save/list.
