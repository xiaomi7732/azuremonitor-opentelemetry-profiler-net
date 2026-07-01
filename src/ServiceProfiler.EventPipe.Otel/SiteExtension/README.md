# Codeless Azure Monitor Profiler — Windows App Service Site Extension (POC)

This is a **proof of concept** that enables the Azure Monitor profiler on a Windows App Service
**without any code change, NuGet reference, or recompile** in the target application. It packages the
profiler as a Kudu/SCM **Site Extension** that injects an ASP.NET Core `IHostingStartup` at process
start. The HostingStartup detects whether the app uses **OpenTelemetry** or the **classic Application
Insights SDK** and enables the matching profiler.

## How it works

The site extension stages three cooperating pieces and sets three environment variables (via
`applicationHost.xdt`) on the application's worker process:

| Component | Assembly | Env var | Role |
|---|---|---|---|
| Resolver hook | `Azure.Monitor.OpenTelemetry.Profiler.StartupHook.dll` | `DOTNET_STARTUP_HOOKS` | Runs before `Main`; installs an `AssemblyLoadContext.Resolving` fallback so the staged payload (which is not on the app's probing path) is loadable. Only fills gaps — the app's own assemblies always win. |
| Activation | `Azure.Monitor.OpenTelemetry.Profiler.HostingStartup.dll` | `ASPNETCORE_HOSTINGSTARTUPASSEMBLIES` | Detects the telemetry stack from the app's own `*.deps.json` and calls `AddAzureMonitorProfiler()` (OpenTelemetry) or `AddServiceProfiler()` (classic Application Insights). |

**Detection is intent-based** (the top-level integration package, with precedence), because the classic
Application Insights ASP.NET Core SDK (2.22+) transitively pulls in the OpenTelemetry SDK, so a naive
"is OpenTelemetry present" check would misclassify a classic app. Order:
1. `Azure.Monitor.OpenTelemetry.AspNetCore` (distro) → OpenTelemetry.
2. `Microsoft.ApplicationInsights.AspNetCore` / `.WorkerService` → Application Insights.
3. Any `OpenTelemetry*` package → OpenTelemetry.
4. Otherwise → nothing enabled.
| Uploader | `Microsoft.ApplicationInsights.Profiler.Uploader.dll` | `SP_UPLOADER_PATH` | The out-of-proc trace uploader the profiler launches to upload captured traces. |

The connection string is taken from the application's existing `APPLICATIONINSIGHTS_CONNECTION_STRING`
app setting.

## Building the package

```powershell
pwsh ./Build-SiteExtension.ps1 -Configuration Release -Version 0.1.0-poc
```

This publishes the HostingStartup closure (both profiler stacks + dependencies), the resolver hook, and
the uploader into `staging/payload/`, then packs everything (plus `applicationHost.xdt`, tagged
`AzureSiteExtension`) into:

```
<repo>/Out/NuGets/Azure.Monitor.OpenTelemetry.Profiler.SiteExtension.<version>.nupkg
```

Package layout:

```
content/
  applicationHost.xdt
  payload/
    Azure.Monitor.OpenTelemetry.Profiler.StartupHook.dll
    Azure.Monitor.OpenTelemetry.Profiler.HostingStartup.dll
    Azure.Monitor.OpenTelemetry.Profiler.dll        (+ classic + all dependencies)
    Uploader/
      Microsoft.ApplicationInsights.Profiler.Uploader.dll (+ dependencies)
```

## Installing on a Windows App Service (manual test)

1. Ensure the app has `APPLICATIONINSIGHTS_CONNECTION_STRING` set (App Service → Configuration).
2. Upload the `.nupkg` to the site's Kudu private feed and install it as a site extension, e.g. via the
   Kudu REST API or by pointing `SCM_SITEEXTENSIONS_FEED_URL` at a folder that contains the package and
   installing `Azure.Monitor.OpenTelemetry.Profiler.SiteExtension` from the SCM **Site Extensions** tab.
3. **Restart** the app so the worker process picks up the injected environment variables.
4. The profiler now activates codelessly on process start.

## Verified locally (smoke test)

Running a published ASP.NET Core app with the extension's environment variables pointed at the built
payload produces:

```
[Azure.Monitor.OpenTelemetry.Profiler.StartupHook] Assembly resolver installed for payload directory: ...
[Azure.Monitor.OpenTelemetry.Profiler.HostingStartup] info: Detected OpenTelemetry. Enabling the Azure Monitor OpenTelemetry profiler.
```

The full profiler DI pipeline then loads and initializes — end-to-end codeless activation, with **no
code change** to the application.

## Profiler support: OpenTelemetry vs classic Application Insights

The package **bundles both profiler stacks** and routes to the one matching the app:

- **OpenTelemetry** (`AddAzureMonitorProfiler`): **verified end-to-end** — the profiler activates and its
  full pipeline initializes with no code change.
- **Classic Application Insights SDK** (`AddServiceProfiler`): **detection and routing are verified** —
  a classic app is correctly identified and the classic profiler is enabled. Full classic activation on
  **.NET 10** currently hits an Application Insights SDK wiring issue in the codeless/early-injection
  context (`TelemetryConfiguration` is not resolvable for the profiler's `AuthTokenProvider`; calling
  `AddApplicationInsightsTelemetry()` from the HostingStartup does not register it and interacts with the
  app's own call). The classic SDK also only supports `[2.23.0, 3.0.0)` — apps on Application Insights
  SDK 3.x are out of range. Full classic-path activation is a follow-up item.

## Known limitations (POC)

- **Runtime version must match the profiler's build.** The bundled profiler references .NET 10 versions
  of `Microsoft.Extensions.*` / `System.Text.Json`. It therefore activates cleanly on **.NET 10** apps
  (the runtime this repo builds against). On **.NET 8 / 9** apps those framework assemblies are present
  at a lower version and the higher-versioned references can't be satisfied by the resolver fallback
  (the lower version is already loaded, so `Resolving` never fires for it). Proper multi-runtime support
  needs dependency-version reconciliation — publishing a payload per target framework, or isolating the
  profiler in a dedicated `AssemblyLoadContext` (tracked as the "adaptive dependency resolution" work in
  the design plan).
- **A valid connection string is required.** With a bogus/placeholder connection string the profiler
  fails to construct its backend client during startup. Use a real `APPLICATIONINSIGHTS_CONNECTION_STRING`.
- **Detection is deps.json-based.** It classifies the stack from the application's own `*.deps.json`
  (developer-chosen packages), which cleanly excludes the bundled payload. Apps that reference neither
  OpenTelemetry nor the Application Insights SDK are intentionally left un-profiled.
- **The `applicationHost.xdt` uses `InsertIfMissing`.** If the app already sets
  `ASPNETCORE_HOSTINGSTARTUPASSEMBLIES` / `DOTNET_STARTUP_HOOKS`, the values must be merged manually.
