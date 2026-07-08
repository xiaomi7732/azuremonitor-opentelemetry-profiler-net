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
| `Microsoft.ApplicationInsights.AspNetCore` / `.WorkerService` **2.x** (legacy classic, **≥ 2.23.0**) | **Classic** — `AddApplicationInsightsTelemetry()` + `AddServiceProfiler()` |
| any `OpenTelemetry*` package (manual OTel) | **OpenTelemetry** — `AddAzureMonitorProfiler()` |
| none of the above | nothing enabled |

This mirrors the profiler's [supported-SDK matrix](../../../README.md): the current
`Microsoft.ApplicationInsights.AspNetCore` (3.x) is an OpenTelemetry-based wrapper, so it uses the
OpenTelemetry profiler; the legacy classic 2.x line uses the classic Application Insights profiler that
this repo also builds (`Microsoft.ApplicationInsights.Profiler.AspNetCore`).

For the classic path, the supported floor is **Application Insights SDK ≥ 2.23.0** (2.22.0 and earlier are
deprecated and unsupported for codeless). The payload compiles the classic bits against 2.23.0; an app on
2.23.0+ loads an assembly version that satisfies that reference (roll-forward). An app on a below-floor,
deprecated SDK does **not** crash — activation is fail-safe (see "Fail-safe activation" below) and simply
disables the profiler.

### Fail-safe activation

Codeless activation must never take down the host application. The `HostingStartup` registers the profiler
inside a deferred `ConfigureServices` callback whose body is isolated behind a non-inlined helper invoked
under try/catch, so even a missing/incompatible-assembly failure — the failure a below-floor dependency
version produces at JIT time — is caught and logged, and the app starts **without** the profiler instead of
crashing. Downstream, the profiler's hosted services also self-contain their startup failures (the classic
`ServiceProfilerAgentBootstrap` catches all activation errors unless `AllowsCrash` is set; the OpenTelemetry
path uses `SafeProfilerHostedService`). The net effect: a below-floor SDK, a bad connection string, or a
future breaking dependency change disables profiling and leaves the application running.

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

This publishes the HostingStartup closure (both profiler stacks + dependencies), the resolver hook, and the
uploader into `staging/payload/<version>/`, builds the XDT transform, and packs everything (plus
`applicationHost.xdt`, tagged `AzureSiteExtension`) into:

```
<repo>/Out/NuGets/Azure.Monitor.OpenTelemetry.Profiler.SiteExtension.<version>.nupkg
```

The payload is built **once** against the lowest supported baseline (.NET 8 / OpenTelemetry 1.8.1 /
`Microsoft.Extensions.*` 8.0); a net8.0 assembly with 8.0 references runs on .NET 8, 9 and 10 (see
"Runtime version support" below).

Package layout:

```
content/
  applicationHost.xdt
  Azure.Monitor.OpenTelemetry.Profiler.SiteExtension.XdtTransforms.dll   (custom XDT transform)
  payload/
    <version>/
      Azure.Monitor.OpenTelemetry.Profiler.StartupHook.dll     (resolver; DOTNET_STARTUP_HOOKS points here)
      Azure.Monitor.OpenTelemetry.Profiler.HostingStartup.dll  (detect + route profiler)
      Azure.Monitor.OpenTelemetry.Profiler.dll        (+ classic profiler + all deps, 8.0 baseline)
      OpenTelemetry.dll (1.8.1), Microsoft.Extensions.*.dll (8.0), ...
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

- **Runtime version support: .NET 8, 9, and 10 — via a single low-baseline payload.** Codeless injection
  runs the profiler *inside* the target app's already-resolved dependency graph. The app (and, for an
  OpenTelemetry app, its own `OpenTelemetry` / `Azure.Core`) loads its shared-framework
  `Microsoft.Extensions.*` **before** our HostingStartup runs, and the runtime can only roll an
  already-loaded assembly **forward** (a higher loaded version satisfies a lower reference), never backward.
  So the profiler must reference the **lowest** versions we support; those roll forward to whatever the app
  loaded. The payload is therefore built **once**, targeting `net8.0`, against an **8.0 baseline**
  (`OpenTelemetry 1.8.1` — its netstandard asset floors `Microsoft.Extensions.*` at 8.0; exporter `1.3.0`
  permits that OTel). A net8.0 assembly + 8.0 refs run on .NET 8 (exact), .NET 9 and .NET 10 (roll-forward).
  These down-level versions apply **only** to the site-extension payload (via `-p:` overrides in
  `Build-SiteExtension.ps1`); the shipped `Azure.Monitor.OpenTelemetry.Profiler` NuGet is unchanged
  (OpenTelemetry 1.15.3).
  - **Minimum app dependency versions (the roll-forward floor):** the target app's **OpenTelemetry must be
    ≥ 1.8.1** and **Azure.Core ≥ ~1.46**. Apps below that are unsupported — an app that pins an *older*
    OpenTelemetry than the profiler was built against would crash with a `FileNotFoundException`, because the
    profiler's reference can't be satisfied by the app's lower, already-loaded copy.
  - **Why not bundle 10.x?** A 10.x payload crashes .NET 8/9 apps: the IIS in-process host loads the app's
    8.0 `Microsoft.Extensions.Logging` before our hook, and the profiler's 10.0 reference can't up-level it
    (verified live). Preloading the 10.x copy first doesn't help — the host already loaded 8.0.
- **A valid connection string is required for the profiler to actually run.** With a
  bogus/placeholder/malformed connection string the profiler cannot reach the backend and disables itself
  (logged), but the application keeps running (fail-safe). Use a real
  `APPLICATIONINSIGHTS_CONNECTION_STRING` for the profiler to capture and upload traces.
- **Classic Application Insights 2.x is supported (≥ 2.23.0) on .NET 8, 9 and 10.** Detection/routing and
  full activation are verified locally on all three runtimes: the classic profiler bootstraps,
  `TelemetryConfiguration` and its dependent services resolve, and (with a real connection string) the agent
  goes Active. The earlier ".NET 10 `TelemetryConfiguration` not resolvable" problem was a downstream symptom
  of a below-floor (2.22) version-skew crash and no longer occurs with the 2.23.0 floor + fail-safe
  activation. End-to-end trace upload on a live App Service is the remaining owner-verified step (as with the
  OpenTelemetry path).
