# Copilot Instructions

## Project Overview

Azure Monitor OpenTelemetry Profiler for .NET — an SDK that captures performance traces from live .NET applications and uploads them to Application Insights. Ships as the `Azure.Monitor.OpenTelemetry.Profiler` NuGet package.

## Architecture

The repo has three layers plus a submodule:

- **Public API** (`src/ServiceProfiler.EventPipe.Otel/Azure.Monitor.OpenTelemetry.Profiler/`) — the NuGet package users reference. Exposes `AddAzureMonitorProfiler()` extension methods on `IOpenTelemetryBuilder` and `IServiceCollection`. Targets `netstandard2.1`.
- **Core** (`src/ServiceProfiler.EventPipe.Otel/Azure.Monitor.OpenTelemetry.Profiler.Core/`) — orchestration, trace collection via EventPipe diagnostics client, and configuration (`ServiceProfilerOptions`). Also targets `netstandard2.1`.
- **Shared** (`src/Microsoft.ApplicationInsights.Profiler.Shared/`) — contracts and services shared between the OTel profiler and the classic Application Insights profiler.
- **Uploader** (`src/ServiceProfiler.EventPipe.Upload/`) — standalone `net8.0` console app that uploads traces to Application Insights. During build, it is published and zipped into the NuGet package.
- **ServiceProfiler submodule** (`ServiceProfiler/`) — internal DevOps submodule containing backend services (stamp, handler, agent, analysis). Requires Git Credential Manager for DevDiv access.

There is also a **classic EventPipe profiler** under `src/ServiceProfiler.EventPipe/` that produces the `Microsoft.ApplicationInsights.Profiler.AspNetCore` NuGet package. The classic and OTel profilers share code via `Microsoft.ApplicationInsights.Profiler.Shared`.

## Solution Files

- `src/Azure.Monitor.OpenTelemetry.Profiler.sln` — the **primary solution** for the OTel profiler (the main project most contributors work on)
- `src/Microsoft.ApplicationInsights.Profiler.sln` — the classic Application Insights profiler
- `src/EventPipe.Profilers.All.sln` — combined solution for both profilers

## Build

Build the OTel profiler solution (requires PowerShell 7):

```powershell
.\src\ServiceProfiler.EventPipe.Otel\tools\BuildSolution.ps1 Debug
```

This restores, builds, publishes the Uploader, and creates the Uploader.zip that gets embedded in the NuGet package.

Build the classic EventPipe profiler and create its NuGet package:

```cmd
src\ServiceProfiler.EventPipe\tools\PackNugetPackage.cmd Debug
```

Pack the OTel profiler NuGet package:

```powershell
.\src\ServiceProfiler.EventPipe.Otel\tools\PackSolution.ps1 -Configuration Debug
```

## Tests

Run all classic profiler tests:

```cmd
src\ServiceProfiler.EventPipe\tools\RunUnitTests.cmd
```

Run a specific test project:

```shell
dotnet test tests\Azure.Monitor.OpenTelemetry.Profiler.Tests
```

Run a single test:

```shell
dotnet test tests\Azure.Monitor.OpenTelemetry.Profiler.Tests --filter "FullyQualifiedName~TestMethodName"
```

Test projects use **xUnit** with **Moq** for mocking.

## Key Conventions

- **Central Package Management** — all NuGet package versions are defined in `Directory.Packages.props` files at root and `src/` levels. Use `<PackageVersion>` in those files, not version attributes in individual `.csproj` files.
- **Public API Analyzers** — enabled by default under `src/ServiceProfiler.EventPipe.Otel/`. Changes to public API surface must update `PublicAPI.Shipped.txt` / `PublicAPI.Unshipped.txt`. Violations of `RS0016` (missing API declaration) and `RS0017` (removed API) are treated as build errors.
- **C# 12 / netstandard2.1** — library projects target `netstandard2.1` with `LangVersion` 12.0, nullable enabled.
- **InternalsVisibleTo** — test projects access internals via `InternalsVisibleTo` with a test signing key.
- **Symbolic links** — the repo uses symlinks. On Windows, clone with `git config core.symlinks true` and run `git reset --hard` in an admin prompt.
- **Git workflow** — never amend commits; always append new commits.
- **Do not modify `ServiceProfiler/src/ServiceProfiler.EventPipe/`** — this path is inside the ServiceProfiler submodule and contains a legacy copy of the uploader and client code. Both the OTel and classic profiler builds use `src/ServiceProfiler.EventPipe.Upload/` (in the main repo) as the uploader source. Changes under the submodule's `ServiceProfiler.EventPipe` path are almost certainly wrong — the code there is not referenced by either build.

## Developer Setup

See `docs/DevEnlistGuide.md` for full details. Key steps:

1. Fork and clone with symlinks enabled: `git config core.symlinks true`
2. Initialize the ServiceProfiler submodule: `git submodule update --init --progress`
3. In an **admin** prompt: `git reset --hard` (to materialize symlinks)
