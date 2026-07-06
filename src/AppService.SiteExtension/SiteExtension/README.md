# Codeless Azure Monitor Profiler — Windows App Service Site Extension (POC)

This is a **proof of concept** that enables the Azure Monitor profiler on a Windows App Service
**without any code change, NuGet reference, or recompile** in the target application. It packages the
profiler as a Kudu/SCM **Site Extension** that injects an ASP.NET Core `IHostingStartup` at process
start. The HostingStartup detects the app's telemetry stack and enables the matching profiler.

## How it works

The site extension stages the payload and sets three environment variables (via `applicationHost.xdt`)
on the application's worker process:

| Component | Assembly | Env var | Role |
|---|---|---|---|
| Resolver hook | `Azure.Monitor.OpenTelemetry.Profiler.StartupHook.dll` | `DOTNET_STARTUP_HOOKS` | Runs before `Main`; installs an `AssemblyLoadContext.Resolving` fallback so the staged payload (which is not on the app's probing path) is loadable. Only fills gaps — the app's own assemblies always win. |
| Activation | `Azure.Monitor.OpenTelemetry.Profiler.HostingStartup.dll` | `ASPNETCORE_HOSTINGSTARTUPASSEMBLIES` | Detects the telemetry stack from the app's own `*.deps.json` and enables the matching profiler. |
| Uploader | `Microsoft.ApplicationInsights.Profiler.Uploader.dll` | `SP_UPLOADER_PATH` | The out-of-proc trace uploader the profiler launches to upload captured traces. |

The connection string is taken from the application's existing `APPLICATIONINSIGHTS_CONNECTION_STRING`
app setting.

### Telemetry-stack detection and routing (version-aware)

Detection reads the app's own `*.deps.json`. It must be **version-aware** because the
`Microsoft.ApplicationInsights.AspNetCore` package id is shared by two very different SDKs, and the
current one transitively pulls in the OpenTelemetry SDK:

| App references | Profiler enabled |
|---|---|
| `Azure.Monitor.OpenTelemetry.AspNetCore` (distro) | **OpenTelemetry** — `AddAzureMonitorProfiler()` |
| `Microsoft.ApplicationInsights.AspNetCore` / `.WorkerService` **3.x** (OpenTelemetry-based) | **OpenTelemetry** — `AddAzureMonitorProfiler()` |
| `Microsoft.ApplicationInsights.AspNetCore` / `.WorkerService` **2.x** (legacy classic) | **Classic** — `AddApplicationInsightsTelemetry()` + `AddServiceProfiler()` |
| any `OpenTelemetry*` package (manual OTel) | **OpenTelemetry** — `AddAzureMonitorProfiler()` |
| none of the above | nothing enabled |

This mirrors the profiler's [supported-SDK matrix](../../../README.md): the current
`Microsoft.ApplicationInsights.AspNetCore` (3.x) is an OpenTelemetry-based wrapper, so it uses the
OpenTelemetry profiler; the legacy classic 2.x line uses the classic Application Insights profiler that
this repo also builds (`Microsoft.ApplicationInsights.Profiler.AspNetCore`).

### Coexistence (applicationHost.xdt append + de-dup)

`ASPNETCORE_HOSTINGSTARTUPASSEMBLIES` and `DOTNET_STARTUP_HOOKS` are semicolon-separated lists that other
agents also write — most notably the **Application Insights auto-instrumentation agent**. The transform
uses a custom `AppendListValueIfMissing` transform (shipped as
`Azure.Monitor.OpenTelemetry.Profiler.SiteExtension.XdtTransforms.dll`) that **appends** our value and
**de-duplicates** it (so re-installs/restarts don't accumulate duplicates), rather than the built-in
`InsertIfMissing` which would be skipped when the variable already exists.

The variables are set under the global `system.webServer/runtime` section, so they apply to every worker
on the site — the same scope the canonical Application Insights / DiagnosticServices extension uses.
(Scoping the env vars to the app pool only would diverge from that canonical pattern and break coexistence:
the AI agent appends its value globally, so a pool-scoped append would not see it. The upgrade/lock concern
that scoping was meant to address is instead solved by the versioned payload folder — see below.)

### Upgrading / uninstalling

Because `DOTNET_STARTUP_HOOKS` is global, both the app worker **and** the persistent .NET-Core SCM/Kudu
worker load `StartupHook.dll` and memory-map (lock) it. To keep Kudu from having to overwrite a locked DLL
during an upgrade, the payload is staged under a **version-stamped subfolder** (`payload/<version>/…`) and
the `applicationHost.xdt` paths are generated with that version baked in. A new package version stages into
a *new* folder, so Kudu writes only new paths and never touches the old, still-locked files — the previous
version's payload is simply orphaned (cleaned up on the next full recycle). This mirrors how the AI agent
versions its own StartupHook path (`…\ApplicationInsightsAgent\<version>\core\StartupHook\…`).

> Historical note: earlier POC builds staged the payload at a fixed `payload/` path, so an in-place upgrade
> failed with *"The process cannot access the file … because it is being used by another process."* and
> required recycling both the app and the SCM/Kudu site first. The versioned layout removes that
> requirement. Orphaned old `payload/<version>/` folders accumulating over many upgrades is a minor known
> limitation (periodic cleanup is future work).

## Building the package

```powershell
pwsh ./Build-SiteExtension.ps1 -Configuration Release -Version 0.1.0-poc
```

This publishes the HostingStartup closure (both profiler stacks + dependencies) once per target framework
(net8.0/net9.0/net10.0, all with the repo-default 10.x deps), plus the resolver hook and the uploader, into
`staging/payload/<version>/`, builds the XDT transform, and packs everything (plus `applicationHost.xdt`,
tagged `AzureSiteExtension`) into:

```
<repo>/Out/NuGets/Azure.Monitor.OpenTelemetry.Profiler.SiteExtension.<version>.nupkg
```

Package layout:

```
content/
  applicationHost.xdt
  Azure.Monitor.OpenTelemetry.Profiler.SiteExtension.XdtTransforms.dll   (custom XDT transform)
  payload/
    <version>/
      Azure.Monitor.OpenTelemetry.Profiler.StartupHook.dll   (single, TFM-agnostic; selects the folder below)
      net8.0/                                                 (one folder per shipped runtime)
        Azure.Monitor.OpenTelemetry.Profiler.HostingStartup.dll
        Azure.Monitor.OpenTelemetry.Profiler.dll        (+ classic profiler + dependencies, 10.x)
      net9.0/
        Azure.Monitor.OpenTelemetry.Profiler.HostingStartup.dll
        Azure.Monitor.OpenTelemetry.Profiler.dll        (+ classic profiler + dependencies, 10.x)
      net10.0/
        Azure.Monitor.OpenTelemetry.Profiler.HostingStartup.dll
        Azure.Monitor.OpenTelemetry.Profiler.dll        (+ classic profiler + dependencies, 10.x)
      Uploader/
        Microsoft.ApplicationInsights.Profiler.Uploader.dll (+ dependencies)
```

## Installing on a Windows App Service (manual test)

1. Ensure the app has `APPLICATIONINSIGHTS_CONNECTION_STRING` set (App Service → Configuration).
2. Upload the `.nupkg` to the site's Kudu private feed and install it as a site extension, e.g. via the
   Kudu REST API or by pointing `SCM_SITEEXTENSIONS_FEED_URL` at a folder that contains the package and
   installing `Azure.Monitor.OpenTelemetry.Profiler.SiteExtension` from the SCM **Site Extensions** tab.
3. **Restart** the app so the worker process picks up the injected environment variables.
4. The profiler now activates codelessly on process start. You can confirm the variables on the app
   worker via Kudu (`/api/processes` → the process whose `APP_POOL_ID` equals the site name, not the
   `~1`-prefixed SCM worker).

## Verified locally (smoke test)

Running a published ASP.NET Core app with the extension's environment variables pointed at the built
payload produces:

```
[Azure.Monitor.OpenTelemetry.Profiler.StartupHook] Assembly resolver installed for payload directory: ...
[Azure.Monitor.OpenTelemetry.Profiler.HostingStartup] info: Detected a supported OpenTelemetry-based telemetry stack. Enabling the Azure Monitor OpenTelemetry profiler.
```

The full profiler DI pipeline then loads and initializes — end-to-end codeless activation, with **no
code change** to the application. The `AppendListValueIfMissing` transform is verified to append + de-dup
+ insert against a sample `applicationHost.config`.

## Known limitations (POC)

- **Runtime version support: .NET 8, 9, and 10.** The build stages one HostingStartup closure per target
  framework (`payload/<version>/net{major}.0/`) and the resolver selects the folder matching the app's
  runtime major (with a highest-≤ fallback). All folders bundle the repo-default **10.x** dependency set
  (`Microsoft.Extensions.*`, `System.Text.Json`, OpenTelemetry, …); those packages ship **net8.0/net9.0/
  net10.0 assets**, so they load and run on all three runtimes — no per-runtime down-leveling is needed. The
  per-TFM folders exist because the HostingStartup **assembly** itself is compiled per target framework
  (`net8.0`/`net9.0`/`net10.0`); the shared `netstandard2.1` profiler libraries are the same across them.
  - A future **.NET 11** app needs **no** rebuild — the resolver's highest-≤ fallback runs it on the
    net10.0 folder and framework roll-forward covers the newer runtime. Adding a native `net11.0` folder is
    a one-line `$targetFrameworks` change when desired.
  - **Correlation caveat (validate on a live .NET 8/9 app):** the profiler correlates CPU samples to
    requests via `System.Diagnostics.DiagnosticSource` (`ActivitySource`/`Activity`). If a lower-runtime app
    has an older `DiagnosticSource` loaded from its shared framework and the bundled 10.x copy ends up as a
    second identity in-process, correlation could be affected. This is the one thing to verify on a real
    .NET 8/.NET 9 OTel app; if it regresses, align **only** `DiagnosticSource` for that TFM via a
    `_SystemDiagnosticsDiagnosticSourceVersion` override in `Build-SiteExtension.ps1` (the per-TFM override
    map is already wired, just empty).
- **A valid connection string is required.** With a bogus/placeholder connection string the profiler
  fails to construct its backend client during startup. Use a real `APPLICATIONINSIGHTS_CONNECTION_STRING`.
- **Legacy classic Application Insights 2.x activation is incomplete.** Detection/routing to the classic
  profiler works, but full activation on .NET 10 still hits an Application Insights SDK wiring issue in
  the early-injection context (`TelemetryConfiguration` not resolvable for the classic profiler's
  `AuthTokenProvider`). The OpenTelemetry path (distro / AI SDK 3.x / manual OTel) is verified end-to-end.
