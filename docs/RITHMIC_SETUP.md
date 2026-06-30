# Rithmic Setup

Flow Terminal integrates with Rithmic **for market data only**. No execution
functionality exists. Live data requires authorized access and entitlements.

## Status
**Blocked (SDK):** the authorized Rithmic R|API+ .NET SDK is proprietary and is not
part of this repository. The adapter (`FlowTerminal.Rithmic`) is fully isolated
behind the `RITHMIC_SDK` compile-time symbol. Until the SDK is installed and wired,
every Rithmic call fails loudly via `RithmicSdkUnavailableException` and the app
runs in mock/replay mode. **No SDK behavior is fabricated.**

## Important: a data subscription is NOT the same as API access

Connecting your **own** app to Rithmic requires **R|API+ programmatic API access**,
which is separate from (and additional to) a market-data subscription:

- Third-party platforms (Sierra Chart, Bookmap, ATAS, Quantower, R|Trader Pro, …)
  are **licensed/conformed Rithmic API applications**. They authenticate with your
  user id + password against a system/gateway using Rithmic's protocol/SDK — there
  is no "no-API" connection.
- Many prop firms/FCMs provision **data-only** logins that work in those approved
  platforms but **do not enable R|API+** for a custom app. (Observed example: a
  funded-account firm using System = its own name, Gateway = Chicago Area, with CME
  depth as a paid add-on, but **no API access** for self-built apps.)
- Before paying for any "Rithmic subscription", confirm with the seller: *"Does this
  include R|API+ developer access and the SDK for a self-developed .NET app for
  personal use, and is API login enabled on my account/system?"* A cheap data plan
  alone usually does not.
- Do **not** attempt to replicate R|Trader Pro by reverse-engineering the Rithmic
  protocol or automating the GUI — it violates Rithmic's terms and this project's
  rules. If a source won't grant R|API+ access, use mock/replay or a vendor with a
  documented self-serve API instead.

The SDK-independent connection resilience the adapter relies on
(`StaleFeedMonitor`, `ReconnectPolicy`, `SubscriptionRegistry`) is already
implemented and tested in `FlowTerminal.MarketData.Resilience`, so once the SDK is
installed only the protocol calls remain to be wired.

## Steps
1. **Obtain authorized access** from Rithmic, your broker, an FCM, a funding
   evaluator, or another authorized provider. Confirm credentials and the
   environment / system / gateway to use.
2. **Confirm entitlements**: CME market data for NQ/ES; market depth (MBP); and
   separately MBO and historical (trades/bars/depth) if you need them.
3. **Install the SDK files** into `lib/rithmic/` exactly as provided (do not rename
   or guess filenames). See [`lib/rithmic/README.md`](../lib/rithmic/README.md).
4. **Reference the assemblies** from `FlowTerminal.Rithmic.csproj` under an
   `EnableRithmic`-guarded `<ItemGroup>`.
5. **Implement the marked region** in `RithmicMarketDataProvider.cs`
   (`#if RITHMIC_SDK`) using the SDK's documented types/callbacks.
6. **Build the Rithmic profile**: `dotnet build -p:EnableRithmic=true`.
7. **Store credentials securely** (Windows Credential Manager / user-secrets).
8. **Verify** in the Diagnostics panel: adapter compiled in, authentication
   succeeded, NQ/ES contracts discovered, live trades and available depth update,
   and missing capabilities shown honestly.

## Capability detection
On connect, the adapter detects and displays the entitled capabilities. Features
gracefully disable when unsupported, e.g.:
- Queue analysis requires **market-by-order**.
- Historical heatmap replay requires **recorded/entitled historical depth**.
- Iceberg results from trades + MBP only are labelled **Estimated**.
- Inferred trade aggressor is labelled **Estimated**.

## Return to mock mode
Build without the flag (`dotnet build`). No proprietary files are required.
