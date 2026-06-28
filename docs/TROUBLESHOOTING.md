# Troubleshooting

| Symptom | Cause / Fix |
|---|---|
| `dotnet` not found | Install the .NET 8 SDK; reopen the terminal. |
| WPF app won't build on Linux/macOS | Expected — the WPF shell is Windows-only. Build `FlowTerminal.Core.slnf` cross-platform; build the full `.sln` on Windows. |
| Build fails mentioning Rithmic | You enabled `-p:EnableRithmic=true` without installing the SDK. Build without the flag for mock mode. See `RITHMIC_SETUP.md`. |
| No market data | In mock mode data is synthetic and always available. For Rithmic, confirm authorized access + entitlements. |
| Trades but no depth | Your feed/entitlement lacks depth; depth-dependent features disable gracefully. |
| Historical data unavailable | Not recorded or not entitled — never manufactured. |
| Wrong contract | Contracts are discovered, not hard-coded; select the intended contract (rollover is user-controlled). |
| Stale feed | Connection-state monitoring marks the feed Stale and reconnects (Phase 8). |
| Sequence gap / invalid book | The pipeline emits a `SequenceGap`, invalidates affected calculations, and resynchronizes from a snapshot. |
| High CPU / GPU / memory | Visual work is rate-limited/coalesced; canonical events are never dropped. See `PERFORMANCE.md`. |
| Disk full / corrupt file | DataRepair (Phase 10) detects partial/corrupt recordings. |
| Layout / settings reset | Workspaces and settings persist locally under `%LOCALAPPDATA%/FlowTerminal`. |
