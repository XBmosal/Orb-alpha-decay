<#
  Publishes Flow Terminal as a self-contained Windows x64 application.
  Run on Windows with the .NET 8 SDK installed:
      pwsh build/publish.ps1                 # framework-dependent
      pwsh build/publish.ps1 -SelfContained  # self-contained single-file
  Output: artifacts/publish/
#>
param(
    [switch]$SelfContained,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out  = Join-Path $root "artifacts/publish"

$common = @(
    "src/FlowTerminal.App/FlowTerminal.App.csproj",
    "-c", $Configuration,
    "-r", "win-x64",
    "-o", $out,
    "/p:Version=0.10.0"
)

if ($SelfContained) {
    dotnet publish @common --self-contained true `
        /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true
} else {
    dotnet publish @common --self-contained false
}

Write-Host "Published to $out" -ForegroundColor Green
Write-Host "Run: $out/FlowTerminal.exe" -ForegroundColor Green
