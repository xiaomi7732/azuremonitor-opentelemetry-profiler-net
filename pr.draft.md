# PR Title

**Improve uploader logging: always surface errors, deprecate UploaderEnvironment**

# PR Description

## Summary

Fix uploader logging so that errors and warnings always surface in the parent process logs without requiring any configuration. Deprecate the broken `UploaderEnvironment` setting.

## Problem

The uploader runs as a separate process, making it hard to debug. The existing `UploaderEnvironment` setting was intended to control uploader logging but had two compounding issues:

1. In Production mode, `logging.ClearProviders()` removed all providers with no replacement — the uploader produced zero stdout, silently swallowing errors
2. Even when logging was enabled (via `UploaderEnvironment=Development`), the parent process logged all captured stdout at `LogDebug` level, invisible at the default `Information` threshold

## Changes

### New `SubprocessLogForwarder` service
- Parses SimpleConsole single-line prefixes (`info:`, `warn:`, `fail:`, `crit:`, `dbug:`, `trce:`) and re-emits each line at the matching `LogLevel`
- Continuation lines (e.g., stack traces) inherit the previous line's level
- Injected into `OutOfProcCaller` to replace the single `LogDebug` dump
- Unit tested with 11 test cases covering prefix parsing, multi-line output, continuation lines, and edge cases

### Uploader `Program.cs`
- Always registers `SimpleConsole` with `SingleLine = true` at `Trace` minimum level
- Suppresses `Microsoft.Hosting.Lifetime` logs (removes misleading "Application is shutting down" noise)
- Removed `IsDevelopment()` conditional and the `Console.WriteLine` debug message

### Deprecation
- `UserConfigurationBase.UploaderEnvironment` marked with `[Obsolete]`
- Removed `--environment` CLI argument from the uploader (`UploadContext`, `UploadContextModel`)
- Removed `Environment` property from `TraceUploaderProxy` upload context construction

### Copilot Instructions
- Added warning that `ServiceProfiler/src/ServiceProfiler.EventPipe/` must not be modified — both profiler builds use `src/ServiceProfiler.EventPipe.Upload/` from the main repo

## Files Changed

| File | Change |
|------|--------|
| `SubprocessLogForwarder.cs` | **New** — log-level-aware forwarding service |
| `OutOfProcCaller.cs` | Uses `SubprocessLogForwarder` instead of `LogDebug` dump |
| `Program.cs` (uploader) | Always registers SimpleConsole; suppresses hosting lifetime |
| `UploadContext.cs` (uploader) | Removed `--environment` CLI option |
| `UploadContextModel.cs` | Removed `Environment` property and serialization |
| `UserConfigurationBase.cs` | `[Obsolete]` on `UploaderEnvironment` |
| `TraceUploaderProxy.cs` | Stopped setting `Environment` on upload context |
| `Profiler.Shared.csproj` | Added `InternalsVisibleTo` for test project |
| `SubprocessLogForwarderTests.cs` | **New** — 11 test cases |
| `UploadContextModelTests.cs` | Removed `--environment` from expected strings |
| `copilot-instructions.md` | Submodule boundary warning |

## Testing

- Both Classic and OTel private packages build successfully
- All tests pass: 21 (OTel) + 58 (Client) + 20 (Upload) = 99 total
- Code reviewed across 4 passes using GPT-5.5 and Claude Opus 4.7 (2 clean iterations)

## Breaking Changes

- `UploaderEnvironment` is deprecated with `[Obsolete]`. Setting it still compiles but has no effect. Users who suppressed `CS0618` globally will see no impact. Users with `TreatWarningsAsErrors` can suppress with `#pragma warning disable CS0618`.
