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

### Coexistence and scoping (applicationHost.xdt)

The `applicationHost.xdt` sets the three environment variables on the **main site's application pool
only** (`system.applicationHost/applicationPools/add name="%XDT_SITENAME%"`), *not* the global
`system.webServer/runtime` section. This deliberately keeps them off the **SCM/Kudu application pool**
(`~1<sitename>`): because `DOTNET_STARTUP_HOOKS` is a file-path hook honored by any .NET-Core process, a
global value would make the .NET-Core Kudu worker load (and memory-map/lock) `StartupHook.dll` from the
extension folder — and since Kudu performs the extension upgrade, it then cannot overwrite a file it has
loaded, breaking upgrades with *"The process cannot access the file … because it is being used by another
process."* Scoping to the app pool ensures only the app worker loads the payload.

`ASPNETCORE_HOSTINGSTARTUPASSEMBLIES` and `DOTNET_STARTUP_HOOKS` are semicolon-separated lists that other
agents also write — most notably the **Application Insights auto-instrumentation agent**. The transform
uses a custom `AppendListValueIfMissing` transform (shipped as
`Azure.Monitor.OpenTelemetry.Profiler.SiteExtension.XdtTransforms.dll`) that **appends** our value and
**de-duplicates** it (so re-installs/restarts don't accumulate duplicates), rather than the built-in
`InsertIfMissing` which would be skipped when the variable already exists.

### Upgrading / uninstalling

Because the app worker loads the payload DLLs from the extension folder, those files are locked while the
app is running. **Stop the app before upgrading or uninstalling the extension**, then start it again:

```powershell
az webapp stop  -g <rg> -n <site>
# upgrade / uninstall the extension
az webapp start -g <rg> -n <site>
```

With the app-pool scoping above, the SCM/Kudu worker no longer holds a lock, so stopping the app is
sufficient. (A fully in-place, no-stop upgrade would additionally require loading the payload from a
versioned shadow copy outside the extension folder — a possible future enhancement.)

## Building the package

```powershell
pwsh ./Build-SiteExtension.ps1 -Configuration Release -Version 0.1.0-poc
```

This publishes the HostingStartup closure (both profiler stacks + dependencies), the resolver hook, and
the uploader into `staging/payload/`, builds the XDT transform, and packs everything (plus
`applicationHost.xdt`, tagged `AzureSiteExtension`) into:

```
<repo>/Out/NuGets/Azure.Monitor.OpenTelemetry.Profiler.SiteExtension.<version>.nupkg
```

Package layout:

```
content/
  applicationHost.xdt
  Azure.Monitor.OpenTelemetry.Profiler.SiteExtension.XdtTransforms.dll   (custom XDT transform)
  payload/
    Azure.Monitor.OpenTelemetry.Profiler.StartupHook.dll
    Azure.Monitor.OpenTelemetry.Profiler.HostingStartup.dll
    Azure.Monitor.OpenTelemetry.Profiler.dll        (+ classic profiler + all dependencies)
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

- **Runtime version must match the profiler's build.** The bundled profiler references .NET 10 versions
  of `Microsoft.Extensions.*` / `System.Text.Json`. It therefore activates cleanly on **.NET 10** apps
  (the runtime this repo builds against). On **.NET 8 / 9** apps those framework assemblies are present
  at a lower version and the higher-versioned references can't be satisfied by the resolver fallback
  (the lower version is already loaded, so `Resolving` never fires for it). Proper multi-runtime support
  needs dependency-version reconciliation — publishing a payload per target framework, or isolating the
  profiler in a dedicated `AssemblyLoadContext` (the "adaptive dependency resolution" work in the design
  plan).
- **A valid connection string is required.** With a bogus/placeholder connection string the profiler
  fails to construct its backend client during startup. Use a real `APPLICATIONINSIGHTS_CONNECTION_STRING`.
- **Legacy classic Application Insights 2.x activation is incomplete.** Detection/routing to the classic
  profiler works, but full activation on .NET 10 still hits an Application Insights SDK wiring issue in
  the early-injection context (`TelemetryConfiguration` not resolvable for the classic profiler's
  `AuthTokenProvider`). The OpenTelemetry path (distro / AI SDK 3.x / manual OTel) is verified end-to-end.
