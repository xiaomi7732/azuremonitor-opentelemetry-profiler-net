# PR Title

**Fix CI test infrastructure: strong-name signing, deadlocks, and ServiceProvider disposal**

# PR Description

## Summary

Fixes CI test infrastructure issues that caused build failures and indefinite test hangs: strong-name signing for Moq/DynamicProxy, test data deployment, EventListener AB-BA deadlocks, and ServiceProvider disposal leaks.

> **Note:** Service Bus support and EventSource ActivityTracker fixes were merged in #131. This PR contains the follow-up CI/test fixes discovered while validating those changes in CI.

## Motivation

After merging #131, CI test runs experienced:
1. **Build failures** — `TypeLoadException` due to `DynamicProxyGenAssembly2` using the wrong strong-name signing key
2. **Test data missing** — `DirectoryNotFoundException` for TestDeployments in Upload tests
3. **Test host hangs** — Undisposed `ServiceProvider` instances kept background threads alive, preventing the test host from exiting
4. **Deadlocks** — AB-BA lock ordering between `EventListener.Dispose()` and `EnableEvents()` caused indefinite hangs

## Changes

### Strong-name signing fix
- **`src/Directory.Build.props`** — Added `CastleCorePublicKey` with the correct public key from Castle.Core's `DynProxy.snk`, used for `InternalsVisibleTo("DynamicProxyGenAssembly2")` in strong-named builds
- **5 project files** — Updated `InternalsVisibleTo` for DynamicProxyGenAssembly2 to use `$(CastleCorePublicKey)` instead of the incorrect product signing key
- **`ServiceProfiler/Signing.props`** (submodule) — Same fix, merged via internal PR

### Test data deployment fix
- **`ServiceProfiler.EventPipe.Upload.Tests.csproj`** — Changed `None Include` → `Content Include` with `Link` metadata so TestDeployments files are correctly copied to the output directory in CI builds

### Solution completeness
- **`EventPipe.Profilers.All.sln`** — Added missing `Azure.Monitor.OpenTelemetry.Profiler.Tests` project

### EventListener deadlock fix
- **`TraceSessionListener.cs`** (production) — Fixed AB-BA deadlock between `EventListener.Dispose()` and `EnableEvents()`. Tasks from `OnEventSourceCreated` are now registered under `lock(_pendingTasks)` atomically with `Task.Run()`, ensuring `Dispose()` always sees in-flight tasks. A volatile `_isDisposing` flag prevents new `EnableEvents()` calls after disposal starts, and `Dispose()` drains pending tasks before calling `base.Dispose()`.
- **`TraceSessionListenerStub.cs`** (test) — Same deadlock fix pattern

### ServiceProvider disposal leaks
- **`ServiceProfilerProviderTests.cs`** — Changed `IServiceProvider` → `ServiceProvider`, wrapped in try/finally with `DisposeAsync()`
- **`ServiceProfilerExtensionTests.cs`** — All `BuildServiceProvider()` calls wrapped in `using` statements; `TelemetryConfiguration` constructed directly instead of resolving from a temporary provider (avoids disposed-singleton bug)
- **`TestsBase.cs`** — Temporary `ServiceProvider` for logger wrapped in `using` block
- **`DiagnosticsClientTraceConfigurationTests.cs`** — Added `using var` for `ServiceProvider`

### Re-enable parallel test execution
- Reverted `DisableTestParallelization` assembly attribute added during investigation, now that the root cause (deadlock + disposal leaks) is fixed

## Testing

- All 29 classic profiler unit tests pass locally
- Code reviewed with GPT 5.5 and Claude Opus 4.7 (2 consecutive clean iterations)

## Risk

- **Low** — Strong-name key fix uses the correct Castle.Core DynProxy.snk public key. Deadlock fix uses standard `volatile` + task draining pattern. ServiceProvider disposal follows .NET best practices.
