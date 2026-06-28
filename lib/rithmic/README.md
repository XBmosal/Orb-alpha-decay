# Rithmic SDK Drop-In Location

This directory is where the **authorized** Rithmic R|API+ .NET SDK files are
installed locally. They are **proprietary** and are **never committed** to this
repository (`.gitignore` excludes everything here except this README and
`.gitkeep`).

Flow Terminal is a strictly **read-only market-data** application. The Rithmic
integration is used **only** for market data (trades, quotes, depth, historical
data, reference data). No execution, order-entry, account, or position
functionality exists or will be added.

---

## 1. Obtain authorized access

You must obtain Rithmic R|API+ access and market-data entitlements through an
authorized source — Rithmic directly, your broker, an FCM, a funding evaluator,
or another authorized provider. Confirm with them:

- API credentials (user, password, and the environment/system/gateway to use).
- Market-data entitlements for CME (NQ, ES).
- Whether you are entitled to **market depth** (market-by-price) and, separately,
  **market-by-order**.
- Whether you are entitled to **historical** trades / bars / depth.

## 2. Install the SDK files here

Place the authorized Rithmic R|API+ .NET SDK assemblies and any required native
files into this directory exactly as provided by your authorized source. **Do not
rename them and do not guess filenames.** This project intentionally does **not**
hard-code Rithmic DLL names, namespaces, classes, or method signatures — wire them
in against the SDK's own documentation.

```
lib/rithmic/
  <authorized SDK assemblies and native files as provided>
  README.md   (this file, committed)
  .gitkeep    (committed)
```

## 3. Reference the SDK from the adapter

In `src/FlowTerminal.Rithmic/FlowTerminal.Rithmic.csproj`, add `<Reference>`
items pointing at the installed assemblies, **guarded** so they are only included
in the Rithmic profile:

```xml
<ItemGroup Condition="'$(EnableRithmic)' == 'true'">
  <Reference Include="<AuthorizedRithmicAssemblyName>">
    <HintPath>..\..\lib\rithmic\<AuthorizedRithmicAssemblyName>.dll</HintPath>
  </Reference>
</ItemGroup>
```

Then implement the marked region in `RithmicMarketDataProvider.cs`
(`#if RITHMIC_SDK …`) using the SDK's **documented** types and callbacks.

## 4. Enable the Rithmic build profile

```powershell
dotnet build -p:EnableRithmic=true
```

This defines the `RITHMIC_SDK` compile-time symbol. Without it, the app builds and
runs normally in **mock / replay** mode and requires none of these files.

## 5. Confirm the adapter loaded

Open **Diagnostics** in the app. `RithmicAvailability.IsCompiledIn` reports `true`
and the connection/capability panel shows the live capabilities detected from your
entitled feed (and honestly shows what is **not** available).

## 6. Return to mock mode

Build without the flag (`dotnet build`) — the default. No proprietary files are
needed and the Rithmic adapter throws `RithmicSdkUnavailableException` if invoked.

---

### Hard rules

- Do **not** commit any Rithmic file other than this README and `.gitkeep`.
- Do **not** store credentials in source, config, logs, or screenshots.
- Do **not** scrape or automate R|Trader Pro or reverse-engineer private protocols.
- Do **not** redistribute Rithmic libraries.
- Do **not** present estimated values as exact exchange data.
