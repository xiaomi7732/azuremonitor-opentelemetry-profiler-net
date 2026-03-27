---
name: build-private-package
description: >
  Build a private NuGet package for the Application Insights Profiler ASP.NET Core SDK.
  Use this skill when asked to build, pack, or create a private or test NuGet package
  for the profiler. Handles restore, build, uploader packing, and NuGet packaging.
compatibility: Windows. Requires .NET SDK, cmd.exe, and PowerShell 7+ (for OTel).
---

# Build Private Application Insights Profiler NuGet Package

## Overview

This skill builds private NuGet packages for the Application Insights Profiler SDK.
Two package flavors are available:

| Flavor | Package(s) | Script |
|--------|-----------|--------|
| **Classic** (ASP.NET Core / App Insights SDK) | `Microsoft.ApplicationInsights.Profiler.Core`, `Microsoft.ApplicationInsights.Profiler.AspNetCore` | `src/ServiceProfiler.EventPipe/tools/PackNugetPackage.cmd` |
| **OTel** (OpenTelemetry / Azure Monitor distro) | `Azure.Monitor.OpenTelemetry.Profiler` | `src/ServiceProfiler.EventPipe.Otel/tools/PackSolution.ps1` |

## Steps

1. **Ask the user which package to build** if not specified:
   - Classic (Application Insights Profiler ASP.NET Core)
   - OTel (Azure Monitor OpenTelemetry Profiler)
2. Ask the user which configuration to use if not specified. Default to `Debug`.
3. Run the appropriate build script (see below).
4. Check output for `Package succeeded :-)` to confirm success.
5. List the generated `.nupkg` files from the output directories and report full paths.

### Classic Package

Run the build script:
```powershell
cmd /c "C:\AIR\fork-otel-profiler\src\ServiceProfiler.EventPipe\tools\PackNugetPackage.cmd <Config>"
```
- First arg (required): `Debug` or `Release`
- Second arg (optional): package type suffix, defaults to `private`
- Third arg (optional): rebuild, `TRUE` (default) or `FALSE`

### OTel Package

Run the build script (requires PowerShell 7+):
```powershell
& "C:\AIR\fork-otel-profiler\src\ServiceProfiler.EventPipe.Otel\tools\PackSolution.ps1" -Configuration <Config>
```
- `-Configuration`: `Debug` or `Release` (default: `Release`)
- `-PackageType`: package type suffix (default: `private`)
- `-Rebuild`: switch to force a rebuild
- `-VersionSuffix`: optional custom version suffix

## Output Locations

### Classic
- With symbols: `src/ServiceProfiler.EventPipe/Out/Nuget/`
- Without symbols: `src/ServiceProfiler.EventPipe/Out/NugetNoSymbols/`

### OTel
- NuGet packages: `Out/NuGets/`

## Version Format

`99.YYYY.MMDD.HHmm-<packageType>YYMMDDHHMM`

## Troubleshooting

- If `PackUploader.cmd` fails (Classic), check that the uploader projects build successfully.
- If NuGet pack fails with missing properties, verify `NuspecProperties` in the csproj
  include all variables referenced in the `.nuspec` files.
- OTel pack requires PowerShell 7+. Run `pwsh --version` to verify.
