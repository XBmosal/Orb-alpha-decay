# Packaging, Upgrade & Data Management

**Status: Config + scripts + DataRepair Complete · Tested; signed MSIX installer
pending.** Phase 10.

## Build a distributable (Windows)
A self-contained x64 build needs no .NET install on the target machine:

```powershell
# framework-dependent (smaller; requires .NET 8 Desktop Runtime on the target)
pwsh build/publish.ps1

# self-contained single file (no runtime needed on the target)
pwsh build/publish.ps1 -SelfContained
```

Output lands in `artifacts/publish/`; launch `FlowTerminal.exe`. The CI `windows`
job builds the full solution; producing a signed MSIX/installer from this publish
output is the remaining packaging step (sign with your own code-signing cert — none
is committed).

## Application data directories
All user data lives under `%LOCALAPPDATA%/FlowTerminal/` (never the install dir):

| Path | Contents |
|---|---|
| `flowterminal.db` | SQLite: settings, workspaces, notes, bookmarks, annotations |
| `data/` | Recorded sessions (Parquet event parts) |
| `logs/` | Serilog daily logs (14-day rolling) |
| `workspaces/` | Saved workspace exports |
| `screenshots/` | Saved chart screenshots |

## Upgrade
Install the new build over the old one. The SQLite schema upgrades in place via
`PRAGMA user_version` migrations (idempotent), so existing settings, workspaces, and
notes are preserved. Recorded Parquet sessions are append-only and forward-compatible.

## Uninstall
Removing the application does **not** delete `%LOCALAPPDATA%/FlowTerminal/`. Delete
that folder manually to remove recordings, settings, and notes.

## Backup & restore
Back up `%LOCALAPPDATA%/FlowTerminal/` (or just `flowterminal.db` for settings/notes
and `data/` for recordings). Restore by copying the folder back before launch.

## Repair corrupt/partial recordings
The `DataRepair` tool detects interrupted writes (leftover `.tmp` files) and
unreadable Parquet parts, and can quarantine them (reversibly — moved to a
`_quarantine` subfolder, never deleted) so the remaining valid parts replay cleanly:

```powershell
# detect only
dotnet run --project tools/FlowTerminal.DataRepair -- <dataRoot> NQ NQZ5 2024-06-03
# detect + quarantine
dotnet run --project tools/FlowTerminal.DataRepair -- <dataRoot> NQ NQZ5 2024-06-03 --fix
```

Exit code 0 = healthy, 1 = issues found/repaired. After repair, `ReplayValidator`
and `DataInspector` confirm the session reads cleanly.

## Disk-space & retention
Recordings are the main consumer of disk. Use a retention policy (delete or archive
old `date=…` folders) and monitor free space; the recorder writes batched part files
and never rewrites a session.
