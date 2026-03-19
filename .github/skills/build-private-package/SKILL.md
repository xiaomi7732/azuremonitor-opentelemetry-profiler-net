---
name: build-private-package
description: >
  Build a private NuGet package for the Application Insights Profiler ASP.NET Core SDK.
  Use this skill when asked to build, pack, or create a private or test NuGet package
  for the profiler. Handles restore, build, uploader packing, and NuGet packaging.
compatibility: Windows. Requires .NET SDK and cmd.exe.
---

# Build Private Application Insights Profiler NuGet Package

## Overview

This skill builds private NuGet packages for the Application Insights Profiler SDK
using the packaging script at `src/ServiceProfiler.EventPipe/tools/PackNugetPackage.cmd`.

Two packages are produced:
- `Microsoft.ApplicationInsights.Profiler.Core` (core library)
- `Microsoft.ApplicationInsights.Profiler.AspNetCore` (ASP.NET Core integration)

## Steps

1. Ask the user which configuration to use if not specified. Default to `Debug`.
2. Run the build script:
   ```powershell
   cmd /c "C:\AIR\fork-otel-profiler\src\ServiceProfiler.EventPipe\tools\PackNugetPackage.cmd <Config>"
   ```
   - First arg (required): `Debug` or `Release`
   - Second arg (optional): package type suffix, defaults to `private`
   - Third arg (optional): rebuild, `TRUE` (default) or `FALSE`
3. Check output for `Package succeeded :-)` to confirm success.
4. List the generated `.nupkg` files from the output directories and report full paths.

## Output Locations

- With symbols: `src/ServiceProfiler.EventPipe/Out/Nuget/`
- Without symbols: `src/ServiceProfiler.EventPipe/Out/NugetNoSymbols/`

## Version Format

`99.YYYY.MMDD.HHmm-<packageType>-YYYYMMDDHHMM`

## Troubleshooting

- If `PackUploader.cmd` fails, check that the uploader projects build successfully.
- If NuGet pack fails with missing properties, verify `NuspecProperties` in the csproj
  include all variables referenced in the `.nuspec` files.
