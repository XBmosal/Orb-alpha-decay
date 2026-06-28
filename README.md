# Flow Terminal

A lightweight, modern, **read-only** order-flow terminal for **NQ** (E-mini
Nasdaq-100) and **ES** (E-mini S&P 500) futures, for personal market analysis and
replay.

> **Read-only by design.** Flow Terminal is strictly a market-data visualization
> and analysis platform. It does **not** submit/modify/cancel orders, connect to
> accounts for execution, show balances/positions/P&L, manage risk, copy trades,
> or simulate fills. There are no buy/sell/flatten/cancel controls anywhere.

`Flow Terminal` is a temporary working name. The implementation and UI are
original and do not copy any proprietary platform's code, branding, or assets.

## Status
**Phase 0 + Phase 1 delivered.** Foundation, domain model, canonical event
pipeline, deterministic mock data, and the Rithmic adapter boundary are in place,
built, and tested. See [`docs/FEATURE_STATUS.md`](docs/FEATURE_STATUS.md).

- 26-project modular monolith (.NET 8, C# 12, WPF + SkiaSharp).
- Cross-platform core (23 projects) builds & tests on Linux/CI; WPF shell on Windows.
- Mock profile builds with **zero** proprietary files.
- Measured pipeline throughput **~872k events/sec** (Debug) with **zero** dropped
  canonical events (goal ≥ 50k).

## Quick start
```powershell
# Cross-platform core + tests (mock profile, no Windows required)
dotnet test FlowTerminal.Core.slnf -c Release

# Full solution incl. the WPF shell (Windows)
dotnet build FlowTerminal.sln -c Release

# Run the desktop app in mock mode (Windows)
dotnet run -c Release --project src/FlowTerminal.App
```

## Documentation
[Architecture](docs/ARCHITECTURE.md) ·
[Installation](docs/INSTALLATION.md) ·
[Market Data Model](docs/MARKET_DATA_MODEL.md) ·
[Rithmic Setup](docs/RITHMIC_SETUP.md) ·
[Order Book](docs/ORDER_BOOK.md) ·
[Footprints](docs/FOOTPRINTS.md) ·
[Detectors](docs/DETECTORS.md) ·
[Replay](docs/REPLAY.md) ·
[Performance](docs/PERFORMANCE.md) ·
[Security](docs/SECURITY.md) ·
[Troubleshooting](docs/TROUBLESHOOTING.md) ·
[User Guide](docs/USER_GUIDE.md) ·
[Feature Status](docs/FEATURE_STATUS.md)

## Technology
.NET 8 LTS · C# 12 · WPF · SkiaSharp · Microsoft.Extensions.Hosting · Serilog ·
SQLite · Apache Parquet · DuckDB · System.Text.Json · xUnit · BenchmarkDotNet.

Live data uses an **authorized** Rithmic R|API+ connection when the SDK,
credentials, and entitlements are available — see
[`lib/rithmic/README.md`](lib/rithmic/README.md). The SDK is proprietary and is
never committed; without it the app runs fully in mock and replay modes.
