# Installation Guide (Beginner-Friendly)

Flow Terminal is a Windows desktop app. The **core libraries and tests** also
build on Linux/macOS, which is how CI validates the mock profile. The **WPF UI**
runs on Windows 10/11 (x64).

## 1. Prerequisites
- Windows 10 or 11, 64-bit (to run the desktop UI).
- [Git](https://git-scm.com/downloads).
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
- One of:
  - Visual Studio 2022 with the **.NET Desktop Development** workload, or
  - VS Code with the **C# Dev Kit** extension.
- An SSD is recommended for recording/replay.

Verify the SDK:
```powershell
dotnet --version   # expect 8.0.x
```

## 2. Get the code
```powershell
git clone <your-fork-url> FlowTerminal
cd FlowTerminal
```

## 3. Build & test

### Cross-platform core (no Windows required, mock profile)
```powershell
dotnet restore FlowTerminal.Core.slnf
dotnet build   FlowTerminal.Core.slnf -c Release
dotnet test    FlowTerminal.Core.slnf -c Release
```

### Full solution incl. the WPF app (Windows)
```powershell
dotnet restore FlowTerminal.sln
dotnet build   FlowTerminal.sln -c Release
dotnet test    FlowTerminal.sln -c Release
```

## 4. Run mock mode (Windows)
```powershell
dotnet run -c Release --project src/FlowTerminal.App
```
The app launches in **Mock** mode with a prominent **SIMULATED DATA** banner and a
**READ-ONLY · NO ORDER ENTRY** indicator. No Rithmic files are required.

Mock mode lets you (as later phases land): load synthetic NQ and ES, record a
session, replay it, use the DOM and footprints and heatmap, and exercise
disconnect / sequence-gap scenarios.

## 5. Run benchmarks
```powershell
dotnet run -c Release --project benchmarks/FlowTerminal.Benchmarks
```

## 6. Rithmic live data (optional)
Live data requires authorized Rithmic R|API+ access and entitlements. See
[`RITHMIC_SETUP.md`](RITHMIC_SETUP.md) and [`lib/rithmic/README.md`](../lib/rithmic/README.md).
Without it, the app stays fully functional in mock and replay modes.

## 7. Packaging (Phase 10)
A self-contained x64 build and installer (MSIX or a Windows installer) are
delivered in Phase 10. Application data, logs, and recordings live under
`%LOCALAPPDATA%/FlowTerminal/`.

## Troubleshooting
See [`TROUBLESHOOTING.md`](TROUBLESHOOTING.md).
