# PR Title

**Migrate from stamp frontend (IProfilerFrontendClient) to diagnostics ingestion service (IProfilerClient)**

# PR Description

## Summary

Replace the legacy stamp frontend client (`IProfilerFrontendClient`) with the new Azure Monitor Diagnostics ingestion service (`IProfilerClient` / `ProfilerClient`) for both settings retrieval and trace upload paths. This applies to both the Classic (App Insights SDK) and OTel (OpenTelemetry) profiler packages.

## Motivation

The stamp frontend is being retired in favor of the diagnostics ingestion service. This migration:
- Simplifies the upload flow from 3 steps (GetStampId → GetEtlUploadAccess → ReportFinish) to 2 steps (GetUploadToken → Commit)
- Uses the v2 generic artifact path instead of the legacy ETL-specific path
- Emits v2 `ServiceProfilerIndex` and `ServiceProfilerSample` custom events compatible with the Portal UX

## Changes

### Upload Path
- Replace 3-step stamp flow with 2-step ingestion flow (`GetProfilerArtifactUploadTokenAsync` → `CommitProfilerArtifactAsync`)
- Derive deterministic artifact ID from `sessionId + machineName` via `XxHash128` for retry idempotency without cross-machine collisions
- Capture `StampId` from the server's `AcceptedArtifact` commit response
- Upload blob with `overwrite: true` for retry safety
- Add `ArtifactId` and `TriggerTime` to blob metadata

### Settings Path
- `RemoteSettingsServiceBase` now accepts `IProfilerClient` instead of `IProfilerFrontendClient`

### Custom Events (ServiceProfilerIndex / ServiceProfilerSample)
- Use v2 `ArtifactLocationProperties` format (AppId + ArtifactId) instead of v1 (stampId + machineName + sessionId)
- Emit all 5 v2 properties required by the Portal UX: `StampId`, `DataCube`, `ArtifactKind`, `ArtifactId`, `Extension`
- Remove unused v1-only properties: `FileId`, `MachineName`, `ProcessId`
- Pass `artifactId` through `IPCAdditionalData` to ensure consistency between agent and uploader

### Interface & DI
- Extract `IProfilerClient` interface in submodule for testability and DI
- Add `STRICT_INTERNAL` guards to `ProfilerClient`, `ProfilerClientOptions`, `ProfilerDiagnosticsClientOptions`
- Register `IProfilerClient` (not concrete `ProfilerClient`) in DI for both Classic and OTel
- Extract `IProfilerClientFactory` for the uploader with required `AgentString` (fallback + warning for non-named-pipe path)

### User Agent
- Differentiate user agent strings: `ServiceProfilerEventPipeAgent-OTel`, `ServiceProfilerEventPipeAgent-Classic`, `EventPipeUploader`

### Cleanup
- Delete `IProfilerFrontendClientBuilder`, `ProfilerFrontendClientBuilder`
- Remove old `FrontendClient` symlinks, add `Azure.Monitor.Diagnostics` symlinks
- Update all 3 solution files
- Deprecate `SkipEndpointCertificateValidation` with `[Obsolete]` + warning log
- Extract `ArtifactIdDerivation` to shared utility

### NuGet Dependencies
- Add `Microsoft.Extensions.Configuration.Binder` to OTel nuspec
- Add `System.IO.Hashing` to both OTel and Classic nuspecs

### Tests
- Migrate all tests from `IProfilerFrontendClient` to `IProfilerClient` / `IProfilerClientFactory`
- Delete obsolete `ProfilerFrontendClientBuilderTests`
- All 107 tests pass (Upload: 20, Client: 58, AI.Profiler: 29)

## Submodule Changes (ServiceProfiler)

PR: [#739994](https://devdiv.visualstudio.com/OnlineServices/_git/ServiceProfiler/pullrequest/739994)
Tag: `otel-profiler-ref-95ea672c8`

- Add `IProfilerClient` interface with `STRICT_INTERNAL` guard
- Add `STRICT_INTERNAL` guards to `ProfilerClient`, `ProfilerClientOptions`, `ProfilerDiagnosticsClientOptions`
- Add `InternalsVisibleTo` entries for consuming assemblies
- Add `CommitProfilerArtifactAsync` metadata overload to `IProfilerClient`

## Testing

- Both Classic and OTel private packages build successfully
- All 107 unit tests pass
- End-to-end tested with `examples/aspnetcore-aisdk3` against live Azure Monitor
- Verified `ServiceProfilerIndex` and `ServiceProfilerSample` custom events emit correct v2 format
- Verified Portal query joins samples with index events correctly
- Code reviewed across 8 passes using Claude Opus 4.7, Claude Sonnet, and GPT 5.5

## Breaking Changes

- `SkipEndpointCertificateValidation` is deprecated and no longer functional (logs warning if set)
