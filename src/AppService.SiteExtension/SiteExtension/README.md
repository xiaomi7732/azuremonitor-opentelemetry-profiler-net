# Codeless Azure Monitor Profiler — App Service (Beta)

This is a **public beta** that enables the Azure Monitor profiler on an Azure App Service
**without any code change, NuGet reference, or recompile** in the target application. It injects an ASP.NET
Core `IHostingStartup` at process start; the HostingStartup detects the app's telemetry stack and enables the
matching profiler. Delivery differs per OS: a Kudu/SCM **Site Extension** on **Windows App Service**, and a
staging **script** (payload to `/home` + App Settings) on **Linux App Service** (blessed .NET stack) — see the
per-platform sections below.

> **Applies to .NET (ASP.NET Core) apps only.** This is a .NET EventPipe profiler. On an App Service
> running Node.js, Python, Java, or PHP the enablement is a **safe no-op**: the injected variables
> (`DOTNET_STARTUP_HOOKS` / `ASPNETCORE_HOSTINGSTARTUPASSEMBLIES`) are .NET-runtime-only and ignored by those
> workers, the `StartupHook` only ever loads inside a .NET process, and even where a .NET process does load
> it detection returns `None` without an app `*.deps.json` and nothing is activated.

## How it works

The site extension stages the payload and sets three environment variables (via `applicationHost.xdt`)
on the application's worker process:

| Component | Assembly | Env var | Role |
|---|---|---|---|
| Resolver + router selector | `Azure.Monitor.OpenTelemetry.Profiler.StartupHook.dll` | `DOTNET_STARTUP_HOOKS` | Runs before `Main`. Detects the app's telemetry stack (a dependency-free `*.deps.json` scan), records the decision, and installs an `AssemblyLoadContext.Resolving` fallback scoped to **only** that stack's payload subfolder (`otel/` or `classic/`) plus the payload root. Only fills gaps — the app's own assemblies always win. |
| Activation (router) | `Azure.Monitor.OpenTelemetry.Profiler.HostingStartup.dll` | `ASPNETCORE_HOSTINGSTARTUPASSEMBLIES` | A **stack-agnostic** router (no reference to either profiler stack). Reads the recorded decision and reflectively invokes the matching per-stack *activator* (`…OpenTelemetryActivator` in `otel/` or `…ClassicActivator` in `classic/`), which enables the real profiler. |
| Uploader | `Microsoft.ApplicationInsights.Profiler.Uploader.dll` | `SP_UPLOADER_PATH` | The out-of-proc trace uploader the profiler launches to upload captured traces. **Shared** by both stacks (both locate it via this variable). |

Each profiler stack lives in its own payload subfolder with its **own** self-contained dependency closure,
so the two stacks' dependencies are never unified — see [Dependency isolation](#dependency-isolation-per-stack-payload-folders).
Only one stack activates per app; the uploader is shared.

The connection string is taken from the application's existing `APPLICATIONINSIGHTS_CONNECTION_STRING`
app setting.

### Telemetry-stack detection and routing (version-aware)

Detection reads the app's own `*.deps.json`. It must be **version-aware** because the
`Microsoft.ApplicationInsights.AspNetCore` package id is shared by two very different SDKs, and the
current one transitively pulls in the OpenTelemetry SDK:

| App references | Profiler enabled |
|---|---|
| `Azure.Monitor.OpenTelemetry.Profiler` **or** `Microsoft.ApplicationInsights.Profiler.AspNetCore` (a profiler NuGet) | **nothing — codeless backs off** (the app activates the profiler in its own code) |
| `Azure.Monitor.OpenTelemetry.AspNetCore` (distro) | **OpenTelemetry** — `AddAzureMonitorProfiler()` |
| `Microsoft.ApplicationInsights.AspNetCore` / `.WorkerService` **3.x** (OpenTelemetry-based) | **OpenTelemetry** — `AddAzureMonitorProfiler()` |
| `Microsoft.ApplicationInsights.AspNetCore` / `.WorkerService` **2.x** (legacy classic, **≥ 2.23.0**) | **Classic** — `AddApplicationInsightsTelemetry()` + `AddServiceProfiler()` |
| any `OpenTelemetry*` package (manual OTel) | **OpenTelemetry** — `AddAzureMonitorProfiler()` |
| none of the above | nothing enabled |

**Back-off when already referenced (checked first).** If the app's `*.deps.json` already references a profiler
NuGet — `Azure.Monitor.OpenTelemetry.Profiler` (OTel) or `Microsoft.ApplicationInsights.Profiler.AspNetCore`
(classic) — the app is expected to call `AddAzureMonitorProfiler()` / `AddServiceProfiler()` itself, so codeless
enablement **backs off and activates nothing** to avoid double activation (a second EventPipe session). The
detection uses the exact `"<pkg>/"` library key, so it does not confuse the public package with its
`Azure.Monitor.OpenTelemetry.Profiler.Core` dependency. (The profiler's own `Add*` registration is also
idempotent, as a secondary guard.)

**App Service App Insights agent, no SDK referenced.** If the app references no supported SDK in its build
but the App Service pre-installed Application Insights agent is instrumenting it at runtime (detected via the
`ApplicationInsightsAgent_EXTENSION_VERSION` app setting), the profiler **cannot** attach to the agent's
telemetry — the agent injects a repacked, below-floor classic SDK (`Microsoft.ApplicationInsights.ILRepack`,
~2.21) whose assembly identity/version our profiler can't bind to. In that case codeless enablement does not
activate and instead logs an actionable recommendation to add the latest
**`Microsoft.ApplicationInsights.AspNetCore`** NuGet (version **3.0 or later**, which is OpenTelemetry-based)
to the app and redeploy; the profiler then activates automatically on the next detection (via the
OpenTelemetry path, since AI AspNetCore 3.x is OpenTelemetry-based).

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

Codeless activation must never take down the host application. The stack-agnostic `HostingStartup` router
has **no compile-time reference to either profiler stack**; inside a deferred `ConfigureServices` callback it
**reflectively** loads and invokes the per-stack activator (`Assembly.Load` → `GetType` → `Enable`), and that
whole reflective invocation is wrapped in try/catch. So even a missing/incompatible-assembly failure — the
failure a below-floor dependency version produces — surfaces as a caught reflection/load exception, is
logged, and the app starts **without** the profiler instead of crashing. Downstream, the profiler's hosted
services also self-contain their startup failures (the classic
`ServiceProfilerAgentBootstrap` catches all activation errors unless `AllowsCrash` is set; the OpenTelemetry
path uses `SafeProfilerHostedService`). The net effect: a below-floor SDK, a bad connection string, or a
future breaking dependency change disables profiling and leaves the application running.

#### Pre-flight dependency-floor check

Before invoking the activator, the router runs a best-effort **pre-flight**: it reads the chosen payload's
`*.deps.json` (which records the exact `assemblyVersion` each shipped assembly was built against — the binding
floors) and compares those to the assemblies the application has **already loaded**. If a loaded shared
dependency is **below** its floor, activation is skipped up front with a specific, actionable log
(*"the application loaded X <ver> but the profiler requires >= <floor>; upgrade X and redeploy"*) instead of
letting activation throw a caught `FileNotFoundException`/`TypeLoadException`. This is **additive** — the
try/catch above remains the backstop for dependencies that load only after the check. Note it flags
dependencies that bump their **assembly** version per release (Azure.Core, `Microsoft.Extensions.*`,
`System.Text.Json`, `System.Diagnostics.DiagnosticSource`); OpenTelemetry pins a fixed `assemblyVersion`
(`1.0.0.0`) across releases, so an OpenTelemetry below the API floor is still caught by the backstop rather
than the pre-flight.

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

> Historical note: earlier builds staged the payload at a fixed `payload/` path, so an in-place upgrade
> failed with *"The process cannot access the file … because it is being used by another process."* and
> required recycling both the app and the SCM/Kudu site first. The versioned layout removes that
> requirement. Orphaned old `payload/<version>/` folders accumulating over many upgrades is a minor known
> limitation (periodic cleanup is future work).

## Building the package

```powershell
pwsh ./Build-SiteExtension.ps1 -Configuration Release -Version 1.0.0-beta.1
```

This publishes the stack-agnostic router + resolver hook into `staging/payload/<version>/`, each profiler
stack into its own subfolder (`otel/` / `classic/`) via a separate publish, and the shared uploader into
`Uploader/`; builds the XDT transform; and packs everything (plus `applicationHost.xdt`, tagged
`AzureSiteExtension`) into:

```
<repo>/Out/NuGets/Azure.Monitor.OpenTelemetry.Profiler.SiteExtension.<version>.nupkg
```

Each stack's payload is built against the lowest supported baseline (.NET 8 / OpenTelemetry 1.8.1 /
`Microsoft.Extensions.*` 8.0); a net8.0 assembly with 8.0 references runs on .NET 8, 9 and 10 (see
"Runtime version support" below).

Package layout:

```
content/
  applicationHost.xdt
  Azure.Monitor.OpenTelemetry.Profiler.SiteExtension.XdtTransforms.dll   (custom XDT transform)
  payload/
    <version>/
      Azure.Monitor.OpenTelemetry.Profiler.StartupHook.dll     (detect + scope resolver per stack)
      Azure.Monitor.OpenTelemetry.Profiler.HostingStartup.dll  (stack-agnostic router)
      Microsoft.AspNetCore.Hosting.dll, Microsoft.Extensions.*.dll  (router's minimal, app-shared closure)
      otel/
        Azure.Monitor.OpenTelemetry.Profiler.HostingStartup.OpenTelemetryActivator.dll
        Azure.Monitor.OpenTelemetry.Profiler.dll (+ OTel profiler closure, its OWN dep versions, 8.0 baseline)
      classic/
        Azure.Monitor.OpenTelemetry.Profiler.HostingStartup.ClassicActivator.dll
        Microsoft.ApplicationInsights.Profiler.AspNetCore.dll (+ classic closure, its OWN dep versions)
      Uploader/
        Microsoft.ApplicationInsights.Profiler.Uploader.dll (+ dependencies)   (SHARED by both stacks)
```

## Dependency isolation (per-stack payload folders)

The OpenTelemetry and classic profiler stacks are **published separately, each into its own subfolder**
(`otel/` / `classic/`) by a distinct `dotnet publish`. This keeps their dependency closures fully
independent: a package that both stacks reference can resolve to a **different version in each folder**
instead of being unified to a single (highest) version.

Concrete example from a built payload — `Microsoft.Diagnostics.NETCore.Client` (referenced by both stacks):

```
otel/    Microsoft.Diagnostics.NETCore.Client.dll -> 0.2.621003
classic/ Microsoft.Diagnostics.NETCore.Client.dll -> 0.2.607501
```

Previously (single flat folder) both were force-unified to `0.2.621003`, running the classic stack on a
version it was not built against. With per-stack folders each keeps the version it shipped with.

How the isolation holds at load time:

- The **router** at the payload root has no compile-time reference to either stack, so publishing it pulls
  neither closure — root contains only the router, the `StartupHook`, and the router's minimal (app-shared)
  hosting closure.
- The `StartupHook` detects the stack up front and scopes the `AssemblyLoadContext.Resolving` fallback to
  **the detected stack's subfolder first, then the payload root**. The *other* stack's subfolder is never on
  the probe path, so the two stacks' assemblies can never be loaded together (and never unify). Only one
  stack activates per app, so this costs nothing.
- The router → activator seam passes the app's `IServiceCollection`
  (`Microsoft.Extensions.DependencyInjection.Abstractions`), a type shared with and owned by the application,
  so it crosses the boundary with the correct identity via roll-forward.

This isolates the two stacks **from each other**. It does not (and cannot) isolate a profiler from the
*app's* telemetry stack: the profiler registers into the app's `TracerProvider` / `TelemetryConfiguration`
and DI, so those shared assemblies (OpenTelemetry, `Microsoft.Extensions.*`, the Application Insights SDK,
`Azure.Core`) still roll forward onto the app's loaded copies — which is exactly what the low-baseline design
(below) ensures.

## Installing on a Windows App Service (manual test)

1. Ensure the app has `APPLICATIONINSIGHTS_CONNECTION_STRING` set (App Service → Configuration).
2. Upload the `.nupkg` to the site's Kudu private feed and install it as a site extension, e.g. via the
   Kudu REST API or by pointing `SCM_SITEEXTENSIONS_FEED_URL` at a folder that contains the package and
   installing `Azure.Monitor.OpenTelemetry.Profiler.SiteExtension` from the SCM **Site Extensions** tab.
3. **Restart** the app so the worker process picks up the injected environment variables.
4. The profiler now activates codelessly on process start. You can confirm the variables on the app
   worker via Kudu (`/api/processes` → the process whose `APP_POOL_ID` equals the site name, not the
   `~1`-prefixed SCM worker).

## Enabling on a Linux App Service (blessed .NET stack)

Linux App Service has **no Kudu site-extension gallery and no `applicationHost.xdt`**, so instead of a
`.nupkg` the same portable payload is delivered as a zip and enabled with a script that stages it under the
app's persistent `/home` and sets the three injection variables as **App Settings**. The payload is identical
to the Windows one (StartupHook + router at the root, `otel/`, `classic/`, shared `Uploader/`); only the
delivery differs.

**Prerequisites**
- Azure CLI (`az`) installed and logged in (`az login`) — cross-platform.
- The app is the **blessed .NET stack** on Linux App Service (custom containers are out of scope — see
  Known limitations), with `/home` persistent storage (default).
- `APPLICATIONINSIGHTS_CONNECTION_STRING` set on the app.

**Build the Linux payload zip** (produced by the same build):

```powershell
pwsh ./Build-SiteExtension.ps1 -Configuration Release -Version 1.0.0-beta.1
# -> <repo>/Out/Linux/AzureMonitorProfiler.1.0.0-beta.1.zip
```

**Enable** (pick either script — identical behavior; `pwsh` runs on Linux/macOS/Windows, so neither is
Windows-only):

```bash
# bash
./enable-linux-appservice.sh -g <resource-group> -n <app-name> [--slot <slot>]
```

```powershell
# PowerShell 7+
pwsh ./Enable-LinuxAppService.ps1 -ResourceGroup <resource-group> -Name <app-name> [-Slot <slot>]
```

The script: resolves the SCM (Kudu) host and an AAD token (works even when SCM basic auth is disabled),
stages the zip to a version-stamped `/home/AzureMonitorProfiler/<version>/` via Kudu
`PUT /api/zip/home/...`, then sets `DOTNET_STARTUP_HOOKS`, `ASPNETCORE_HOSTINGSTARTUPASSEMBLIES`
(**append + de-duplicate**, so it coexists with the platform App Insights agent / user-set hooks) and
`SP_UPLOADER_PATH` via `az webapp config appsettings set` — which restarts the app. Confirm activation in the
log stream:

```
[Azure.Monitor.OpenTelemetry.Profiler.StartupHook] Detected telemetry stack: OpenTelemetry
[Azure.Monitor.OpenTelemetry.Profiler.HostingStartup] info: ... Enabling the ... profiler.
```

**Dependencies to run the enable step:** only `az` (logged in) and `curl` (bash) / built-in `Invoke-RestMethod`
(pwsh). **No .NET SDK and no Windows are required** — the script uploads a prebuilt zip and sets App Settings.
Building the zip is a separate step (needs the .NET SDK + `pwsh`, on any OS).

**Uninstall:** re-run with `--disable` (bash) / `-Disable` (pwsh) — it removes only our env-var entries
(de-dup-aware) and leaves the staged `/home` folder for manual cleanup.

**Notes:** `/home` is persisted and shared across all scale-out instances, so a single stage covers every
instance; App Settings apply site-wide. Live end-to-end trace upload from a Linux App Service is verified;
payload load, telemetry-stack detection, per-stack resolver, and activation for both stacks were validated on
Linux.

## Verified

**Live App Service (end-to-end).** On a Windows App Service, both an OpenTelemetry app and a classic
Application Insights 2.x app activate codelessly and their captured traces upload and appear in Application
Insights — no code change to the application.

**Local smoke test.** Running a published ASP.NET Core app with the extension's environment variables pointed
at the built payload produces (OpenTelemetry app shown; a classic Application Insights 2.x app routes to
`classic/` and enables the classic profiler symmetrically):

```
[Azure.Monitor.OpenTelemetry.Profiler.StartupHook] Detected telemetry stack: OpenTelemetry
[Azure.Monitor.OpenTelemetry.Profiler.StartupHook] Assembly resolver installed. Probe order: ...\payload\<version>\otel, ...\payload\<version>
[Azure.Monitor.OpenTelemetry.Profiler.HostingStartup] info: Detected a supported OpenTelemetry-based telemetry stack. Enabling the Azure Monitor OpenTelemetry profiler.
```

Note the probe order lists only the detected stack's subfolder plus the payload root — the other stack's
folder is never on the path, so the two stacks stay isolated (see
[Dependency isolation](#dependency-isolation-per-stack-payload-folders)). The full profiler DI pipeline then
loads and initializes — end-to-end codeless activation, with **no code change** to the application. The
`AppendListValueIfMissing` transform is verified to append + de-dup + insert against a sample
`applicationHost.config`.

## Diagnostics (startup file logging)

The codeless injection runs very early (the `StartupHook` before `Main`, the router during host build), so its
diagnostics are easy to miss in the log stream. Set the **`SP_STARTUP_LOG`** environment variable to also
capture the full StartupHook → HostingStartup trace to a file:

- unset / empty → **off** (stdout + `Trace` only; the default).
- `1` or `true` → **on**, default path `<HOME>/LogFiles/AzureMonitorProfiler/startup_<pid>.log` (`HOME` is the
  App Service persistent root: `D:\home` on Windows, `/home` on Linux — so the file is readable via Kudu and
  log streaming). Falls back to the temp dir when `HOME` is unset.
- any other value → treated as an explicit log file path.

Each line is `UTC-timestamp [PID <pid>] <level> <component>: <message>`; files are **per-PID**, so the app
worker and the always-.NET SCM/Kudu worker never contend. All file IO is fail-safe — a failure disables the
file sink and falls back to stdout/`Trace`; it never affects host startup.

Enable it on **Windows App Service** by adding the `SP_STARTUP_LOG` app setting (Configuration → Application
settings). On **Linux App Service**, pass `--debug` (`enable-linux-appservice.sh`) / `-DebugLog`
(`Enable-LinuxAppService.ps1`), which sets `SP_STARTUP_LOG=1` for you.

## Known limitations (beta)

- **.NET apps only.** This profiler targets .NET / ASP.NET Core. On non-.NET App Service stacks
  (Node.js, Python, Java, PHP) the enablement does nothing — a safe no-op (see "Applies to" above). Supporting
  those runtimes would require entirely different profiling technology and is out of scope.
- **Platforms: Windows App Service (site extension) and Linux App Service blessed .NET stack (enable script).**
  Linux **custom containers** and non-App-Service container hosts are out of scope for this beta (no `/home`
  site-extension model; stage the payload in the image and set the env vars as container env vars instead).
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
    ≥ 1.8.1** and **Azure.Core ≥ 1.46.1** (the payload is built against Azure.Core 1.46.1 — the lowest the
    dependency graph allows, since Azure.Identity 1.14.0 floors it there). Apps below that are unsupported —
    an app that pins an *older* OpenTelemetry or Azure.Core than the profiler was built against would crash
    with a `FileNotFoundException`, because the profiler's reference can't be satisfied by the app's lower,
    already-loaded copy.
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
  activation. Live App Service end-to-end trace upload is verified for the classic path (as for the
  OpenTelemetry path).
