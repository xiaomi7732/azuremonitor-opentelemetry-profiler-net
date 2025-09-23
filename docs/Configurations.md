# Service Profiler Configuration Guide

This document summarizes configuration options exposed via `UserConfigurationBase` (base for `ServiceProfilerOptions`). These are typically set when initializing the profiler in your application (e.g., through dependency injection or manual construction).

## Core Session Controls

| Property | Type | Default | Description |
|---------|------|---------|-------------|
| BufferSizeInMB | int | 250 | In-memory circular buffer size for trace data (approx). Not a cap on final trace file size. Increase only if traces truncate prematurely. |
| Duration | TimeSpan | 30s | Length of a single profiling session. Keep short in production to control overhead. |
| InitialDelay | TimeSpan | 0 | Delay before first profiling session. Useful to avoid startup noise. |
| ConfigurationUpdateFrequency | TimeSpan | 5s | Polling interval for updated triggers/remote settings. Raising reduces traffic; lowering increases responsiveness. |
| ActivatedOnStart | bool | true | If false, agent stays idle (fetching settings) until remote activation. |

## Enable / Disable Toggles

| Property | Default | Meaning |
|----------|---------|---------|
| IsDisabled | false | Master switch. When true, profiler does nothing. |
| IsSkipCompatibilityTest | false | Forces profiling even if environment checks fail. Use only for diagnostics. |
| StandaloneMode | false | Runs without contacting the backend service (no uploads, local only). |
| AllowsCrash | false | If true, unhandled exceptions in profiler surface (process may crash) to aid debugging. |

## Upload & Storage

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| Endpoint | string? | null | Override service endpoint. Leave null for production default. |
| UploadMode | enum | OnSuccess | Options: Never, OnSuccess, Always. Use non-default only for debugging. |
| PreserveTraceFile | bool | false | Keeps local trace after successful upload. |
| LocalCacheFolder | string | OS temp path | Override temp directory for trace staging. |
| SkipEndpointCertificateValidation | bool | false | Disables TLS validation. Only for secure, controlled test environments. |
| UploaderEnvironment | string | "Production" | Tag to indicate environment (e.g., "Canary"). |

## Random / Triggered Profiling

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| RandomProfilingOverhead | float | 0.01 (1%) | Target average time spent profiling per hour. Effective number of sessions per hour ≈ (60 * overhead) / DurationMinutes. |
| CPUTriggerThreshold | float | 80 | Avg CPU (%) threshold to start a CPU-triggered session. |
| CPUTriggerCooldown | TimeSpan | (default per CpuTriggerSettings) | Minimum wait after a CPU-triggered session. |
| MemoryTriggerThreshold | float | 80 | Avg memory usage (%) threshold to start a memory-triggered session. |
| MemoryTriggerCooldown | TimeSpan | (default per MemoryTriggerSettings) | Minimum wait after memory-triggered session. |

## Advanced / Extensibility

| Property | Type | Description |
|----------|------|-------------|
| CustomEventPipeProviders | IEnumerable<EventPipeProviderItem> | Adds extra EventPipe providers. Use cautiously—can greatly increase volume and overhead. |
| NamedPipe | NamedPipeOptions | IPC channel customization (diagnostics / tooling). |
| TraceScavenger | TraceScavengerServiceOptions | Controls cleanup policy for local traces (age, limits, etc.). |

## Telemetry & Privacy

| Property | Default | Notes |
|----------|---------|-------|
| ProvideAnonymousTelemetry | false | Enables anonymous usage telemetry to Microsoft. |

## Tuning Guidelines

1. Start with defaults.
2. Increase `BufferSizeInMB` only if logs show truncation or gaps.
3. Keep `Duration` ≤ 60s for production; shorter sessions reduce disruption.
4. Avoid changing `UploadMode` except during controlled troubleshooting.
5. Use triggers (CPU/Memory thresholds) to supplement, not replace, random sampling.
6. Set `StandaloneMode = true` only for offline/local investigations.

## Example (Programmatic Setup)

```csharp
var options = new ServiceProfilerOptions
{
    Duration = TimeSpan.FromSeconds(45),
    RandomProfilingOverhead = 0.02f, // target ~2% time
    CPUTriggerThreshold = 85f,
    MemoryTriggerThreshold = 85f,
    PreserveTraceFile = true,
    LocalCacheFolder = "/var/profiler-cache",
    CustomEventPipeProviders = new []
    {
        new EventPipeProviderItem
        {
            Name = "MyCompany-Diagnostics",
            Level = 4, // Verbose
            Keywords = "0xFFFFFFFFFFFFFFFF"
        }
    }
};
```

## Overhead Estimation

Effective sessions per hour (random):
sessions ≈ (60 * RandomProfilingOverhead) / (DurationMinutes)

Example: overhead=0.01, duration=0.5 min (30s) → (60 * 0.01)/0.5 = 1.2 sessions/hour.

## Safety Checklist

- Production: Keep `AllowsCrash = false`.
- Do not disable certificate validation outside isolated test networks.
- Review storage footprint if `PreserveTraceFile = true`.
- Validate custom providers in staging before production rollout.

## Troubleshooting Quick Reference

| Symptom | Suggested Adjustment |
|---------|----------------------|
| Few or no profiles | Lower thresholds, slightly raise RandomProfilingOverhead |
| High overhead | Shorter Duration, reduce CustomEventPipeProviders |
| Large disk usage | Disable PreserveTraceFile, tighten scavenger options |
| Missing triggers | Verify thresholds below observed steady-state levels |

## Versioning / Evolution

Defaults may evolve; pin critical behavior via explicit settings to avoid surprises during upgrades.

