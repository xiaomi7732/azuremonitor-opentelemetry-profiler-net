# PR Title

**Add Service Bus support, fix EventSource ActivityTracker nesting, fix CI test infrastructure**

# PR Description

## Summary

This PR makes three categories of changes:

1. **Service Bus support** — Adds Azure Service Bus processor activity instrumentation to the OTel profiler
2. **EventSource ActivityTracker fix** — Prevents ActivityTracker push/pop from corrupting relay event ActivityId paths across all profiler variants
3. **CI test infrastructure fixes** — Fixes strong-name signing for Moq/DynamicProxy, test data deployment, EventListener deadlocks, and ServiceProvider disposal leaks that caused test host hangs

## Motivation

The profiler previously only captured ASP.NET Core HTTP request activities. Applications using Azure Service Bus processors had no visibility into message processing performance. This PR extends the profiler to instrument Service Bus processing events, enabling profiling traces for message-driven workloads.

Additionally, CI test runs were hanging indefinitely due to undisposed `ServiceProvider` instances and an AB-BA deadlock between `EventListener.Dispose()` and `EnableEvents()`. These infrastructure fixes ensure reliable CI test execution.

## Changes

### 1. Service Bus support (OTel profiler)

- **`ServiceBusActivityIdResetListener`** — New `ActivityListener` that resets the thread-local EventSource `ActivityId` when Service Bus processor activities start, preventing the Service Bus SDK's internal activity from contaminating the relay event's `ActivityId` path
- **`DiagnosticSourceEventSourceHandler`** — Extended to recognize Service Bus processor activity names
- **`RequestActivityRelay`** — Generalized to handle both HTTP and Service Bus request activities, with deduplication across event source handlers
- **`TraceSessionListenerFactory`** — Registers the new `ServiceBusActivityIdResetListener` via DI
- **`DiagnosticsClientTraceConfiguration`** — Added `Azure.Messaging.ServiceBus` EventSource to the EventPipe provider list

### 2. EventSource ActivityTracker fix (all profilers)

- **`AzureMonitorOpenTelemetryProfilerDataAdapterEventSource`** (OTel) — Added `Task = Tasks.Request` and `ActivityOptions = EventActivityOptions.Disable` on `RequestStart`/`RequestStop` events, preventing EventSource's `ActivityTracker` from pushing/popping the thread's `ActivityId` during `WriteEvent`
- **`ApplicationInsightsDataRelayEventSource`** (Classic) — Changed `ActivityOptions` from `None` to `Disable`, matching the v3.0 EventSource pattern
- **`TraceSessionListener` / `TraceSessionListener30`** (Classic) — Save and restore `CurrentThreadActivityId` around relay calls, matching the pattern used by the OTel `RequestActivityRelay`
- **`SampleActivity.IsValid`** (Shared) — Updated comment to reflect the new invariant; kept `StartsWith` for backward compatibility

### 3. CI test infrastructure fixes

#### Strong-name signing fix
- **`src/Directory.Build.props`** — Added `CastleCorePublicKey` with the correct public key from Castle.Core's `DynProxy.snk`, used for `InternalsVisibleTo("DynamicProxyGenAssembly2")` in strong-named builds
- **5 project files** — Updated `InternalsVisibleTo` for DynamicProxyGenAssembly2 to use `$(CastleCorePublicKey)` instead of the incorrect product signing key
- **`ServiceProfiler/Signing.props`** (submodule) — Same fix, merged via internal PR

#### Test data deployment fix
- **`ServiceProfiler.EventPipe.Upload.Tests.csproj`** — Changed `None Include` → `Content Include` with `Link` metadata so TestDeployments files are correctly copied to the output directory in CI builds

#### Solution completeness
- **`EventPipe.Profilers.All.sln`** — Added missing `Azure.Monitor.OpenTelemetry.Profiler.Tests` project

#### EventListener deadlock fix
- **`TraceSessionListener.cs`** (production) — Fixed AB-BA deadlock between `EventListener.Dispose()` and `EnableEvents()`. Tasks from `OnEventSourceCreated` are now registered under `lock(_pendingTasks)` atomically with `Task.Run()`, ensuring `Dispose()` always sees in-flight tasks. A volatile `_isDisposing` flag prevents new `EnableEvents()` calls after disposal starts, and `Dispose()` drains pending tasks before calling `base.Dispose()`.
- **`TraceSessionListenerStub.cs`** (test) — Same deadlock fix pattern

#### ServiceProvider disposal leaks
- **`ServiceProfilerProviderTests.cs`** — Changed `IServiceProvider` → `ServiceProvider`, wrapped in try/finally with `DisposeAsync()`
- **`ServiceProfilerExtensionTests.cs`** — All `BuildServiceProvider()` calls wrapped in `using` statements; `TelemetryConfiguration` constructed directly instead of resolving from a temporary provider (avoids disposed-singleton bug)
- **`TestsBase.cs`** — Temporary `ServiceProvider` for logger wrapped in `using` block
- **`DiagnosticsClientTraceConfigurationTests.cs`** — Added `using var` for `ServiceProvider`

#### Re-enable parallel test execution
- Reverted `DisableTestParallelization` assembly attribute added during investigation, now that the root cause (deadlock + disposal leaks) is fixed

## Testing

- All 29 classic profiler unit tests pass locally
- OTel private NuGet package builds successfully
- Code reviewed with GPT 5.5 and Claude Opus 4.7 (2 consecutive clean iterations, multiple rounds)

## Risk

- **Low (Service Bus + ActivityTracker)** — The `EventActivityOptions.Disable` pattern is proven in `ApplicationInsightsDataRelayEventSource30` (used since .NET Core 3.0). The save/restore of `CurrentThreadActivityId` matches the OTel relay's existing pattern.
- **Low (CI fixes)** — Strong-name key fix uses the correct Castle.Core DynProxy.snk public key. Deadlock fix uses standard `volatile` + task draining pattern. ServiceProvider disposal follows .NET best practices.
