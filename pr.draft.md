# PR Title

**Fix IPC serialization robustness and improve diagnostics for trace upload**

# PR Description

## Summary

Fixes a potential `UnsupportedPayloadTypeException` during named pipe IPC between the profiler and uploader, and improves diagnostic logging for serialization failures.

## Motivation

A user reported repeated upload failures with `UnsupportedPayloadTypeException: Can't serialize the message object` on Windows App Service (OTel profiler, Entra ID auth). The error message provided no information about which type failed or why, making diagnosis impossible.

Investigation revealed two robustness issues:
1. The profiler serializes the raw `Azure.Core.AccessToken` struct over the named pipe. This struct is external and its properties change across versions (e.g., Azure.Core 1.51+ added `BindingCertificate` of type `X509Certificate2?`, which is not JSON-serializable when non-null).
2. `TrySerialize` only caught `JsonException`, but `System.Text.Json` can also throw `NotSupportedException` (unsupported types like `X509Certificate2`) and `ArgumentException` (invalid UTF-16 in string data from malformed traces).

## Changes

### Serialization robustness

- **`AccessTokenData.cs`** — **New** serialization-safe DTO with only `Token` and `ExpiresOn`, replacing raw `AccessToken` struct serialization over the named pipe
- **`PostStopProcessor.cs`** — Converts `AccessToken` → `AccessTokenData` before `SendAsync`, eliminating dependency on Azure.Core's evolving struct layout
- **`HighPerfJsonSerializationProvider.cs`** — Catches `NotSupportedException` and `ArgumentException` alongside `JsonException` in `TrySerialize`/`TryDeserialize`; added `ILogger` (with backward-compatible parameterless constructor) to log caught exceptions

### Diagnostics

- **`DuplexNamedPipeService.cs`** — `SendAsync` error message now includes `typeof(T).FullName` so the failing type is immediately visible in logs

### Tests

- **`AuthTokenContractTests.cs`** — Added 2 tests verifying `AccessTokenData` → `AccessTokenContract` wire compatibility (populated and default values)

### ServiceProvider disposal leaks
- **`ServiceProfilerProviderTests.cs`** — Changed `IServiceProvider` → `ServiceProvider`, wrapped in try/finally with `DisposeAsync()`
- **`ServiceProfilerExtensionTests.cs`** — All `BuildServiceProvider()` calls wrapped in `using` statements; `TelemetryConfiguration` constructed directly instead of resolving from a temporary provider (avoids disposed-singleton bug)
- **`TestsBase.cs`** — Temporary `ServiceProvider` for logger wrapped in `using` block
- **`DiagnosticsClientTraceConfigurationTests.cs`** — Added `using var` for `ServiceProvider`

| File | Change |
|------|--------|
| `Contracts/AccessTokenData.cs` | **New** — serialization-safe DTO for AccessToken |
| `Services/PostStopProcessor.cs` | Use `AccessTokenData` DTO instead of raw `AccessToken` |
| `Services/HighPerfJsonSerializationProvider.cs` | Broader exception handling + ILogger |
| `Services/IPC/DuplexNamedPipeService.cs` | Include type name in error message |
| `ServiceProfiler.EventPipe.Upload.csproj` | Link `AccessTokenData.cs` |
| `AuthTokenContractTests.cs` | 2 new wire-compatibility tests |

## Testing

- All 22 uploader tests pass (including 2 new)
- All 21 OTel profiler tests pass
- Full solution build succeeds
- Code reviewed with GPT 5.5 and Claude Opus 4.7 (2 consecutive clean iterations)

## Risk

- **Low** — The wire format is backward-compatible: `AccessTokenData` serializes the same `Token`/`ExpiresOn` properties the uploader's `AccessTokenContract` already expects. Extra properties from the old `AccessToken` payload were always ignored by the uploader.
