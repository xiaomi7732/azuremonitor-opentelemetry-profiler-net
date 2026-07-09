# Azure Monitor Profiler (Codeless)

Enable the Azure Monitor profiler on a **Windows App Service** with **no code change, no NuGet reference, and
no recompile**. This site extension injects an ASP.NET Core hosting startup that detects your app's telemetry
stack at process start and enables the matching profiler.

> **Public beta.**

## Supported apps

- **OpenTelemetry-based .NET apps** — the Azure Monitor OpenTelemetry distro, the OpenTelemetry-based
  Application Insights SDK 3.x, or a manual OpenTelemetry setup.
- **Classic Application Insights .NET apps** — Application Insights SDK 2.x (≥ 2.23.0).

**.NET / ASP.NET Core only.** On non-.NET Windows App Service stacks (Node.js, Python, Java, PHP) the
extension is a safe no-op. Runtimes: .NET 8, 9, and 10.

## Install

1. Ensure your app has `APPLICATIONINSIGHTS_CONNECTION_STRING` set.
2. Install this extension from the App Service **Site Extensions** (SCM/Kudu) gallery.
3. Restart the app so the worker picks up the profiler.

The profiler then activates automatically and uploads traces to Application Insights / Azure Monitor.

## Learn more

- OpenTelemetry profiler: https://github.com/Azure/azuremonitor-opentelemetry-profiler-net
- Classic Application Insights profiler: https://github.com/microsoft/ApplicationInsights-Profiler-AspNetCore
