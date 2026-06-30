# Security

## Read-only by design
Flow Terminal has **no execution capability**. It cannot submit, modify, or cancel
orders, connect to brokerage accounts for execution, display balances/positions/
P&L, manage risk, copy trades, or simulate fills. There are no buy/sell/flatten/
reverse/cancel controls anywhere in the codebase. This is an architectural
guarantee, not a configuration toggle (`AppInfo.IsReadOnly == true`).

## Credentials are never persisted in the repo
Credentials must **never** appear in source, Git, committed configuration, logs,
crash reports, screenshots, or notes exports. Use:
- **Windows Credential Manager** (production), or
- **.NET user-secrets** (`dotnet user-secrets`) during development, behind an
  encrypted secrets abstraction.

`appsettings.example.json` contains **placeholders only**. Copy it to
`appsettings.local.json` (gitignored) for local settings — but still keep real
secrets out of files.

## .gitignore protections
The repository ignores credentials, the proprietary Rithmic SDK files
(`lib/rithmic/**` except its README), local databases, private market-data
recordings (`data/`, `*.parquet`, `*.duckdb`), build outputs, logs, crash dumps,
installers, and user workspaces.

## Proprietary data & SDK
- Do not commit, redistribute, or reverse-engineer Rithmic libraries or protocols.
- Do not scrape or automate R|Trader Pro.
- Do not present synthetic data as real exchange data — mock mode always shows
  **SIMULATED DATA**.
- Do not present estimated/inferred values as exact exchange data — they are
  labelled **Estimated**.
