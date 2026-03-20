# CPU Usage Monitoring

This document explains how the profiler monitors CPU usage to trigger profiling sessions when CPU exceeds a configured threshold.

## Overview

The CPU monitoring pipeline has three layers:

1. **Metrics Sampling** — samples raw CPU usage every 1 second
2. **Baseline Tracking** — aggregates samples into a rolling average every 30 seconds
3. **Scheduling Policy** — compares the average against the threshold every 5 seconds

```
CPU Sampler (1s interval)
        ↓
Baseline Tracker (30s rolling average)
        ↓
CPU Monitoring Policy (5s polling)
        ↓ cpuUsage > threshold?
Start Profiling Session
```

## How CPU is Measured

### Windows

The profiler uses `System.Diagnostics.Process.TotalProcessorTime` to measure CPU consumption of the current process. CPU percentage is calculated as:

```
CPU% = (cpuTimeDelta / (wallTimeDelta × processorCount)) × 100
```

### Linux

On Linux (including Azure App Service containers), the profiler reads CPU usage from the cgroup filesystem, which provides accurate per-container metrics in sandboxed environments:

- **cgroup v2**: reads `usage_usec` from `/sys/fs/cgroup/cpu.stat`
- **cgroup v1** (fallback): reads from `/sys/fs/cgroup/cpuacct/cpuacct.usage`

CPU percentage is calculated as:

```
CPU% = (cpuTimeDelta / (wallTimeDelta × effectiveCpuCount)) × 100
```

The **effective CPU count** is derived from the container's cgroup CPU quota rather than `Environment.ProcessorCount` (which may return the host's total core count). This prevents CPU% from being under-reported in containers with CPU limits. Fractional quotas (e.g., 1.5 cores from Kubernetes `cpu: "1500m"`) are preserved as-is for accurate normalization:

- **cgroup v2**: reads quota and period from `/sys/fs/cgroup/cpu.max`
- **cgroup v1**: reads `cpu.cfs_quota_us` / `cpu.cfs_period_us`
- **Fallback**: `Environment.ProcessorCount` when no quota is configured

The sampler runs on a dedicated background thread to ensure accurate readings even when the application is under heavy CPU load. If a sampling iteration fails (e.g., transient IO error), it logs an error and retries on the next 1-second interval.

## Baseline Tracking (Sliding Window)

Raw 1-second samples are aggregated into a smoothed average using a circular buffer with a sliding window.

### Configuration

| Setting | Default | Description |
|---------|---------|-------------|
| `CpuRollingHistorySize` | 10 minutes | Total window size for the circular buffer |
| `CpuRollingHistoryInterval` | 30 seconds | How often a sample is recorded into the buffer |
| `CpuAverageWindow` | 30 seconds | Sliding window for computing the reported average |

### How it works

1. Every 30 seconds, the latest CPU sample is recorded into a circular buffer (sized at `10min / 30s = 20 slots`).
2. The average is computed over the most recent `CpuAverageWindow` (30 seconds) of entries.
3. This average is what the scheduling policy uses for threshold comparison.

This smoothing prevents brief CPU spikes from triggering profiling while still detecting sustained high CPU usage.

## CPU Monitoring Scheduling Policy

The policy checks the CPU baseline every 5 seconds:

- If `cpuUsage > CpuThreshold` → starts a profiling session, then enters a cooldown period.
- If `cpuUsage ≤ CpuThreshold` → stands by for 5 seconds, then checks again.

### Configuration

These settings can be configured via app settings or environment variables:

| Setting | Default | App Setting Key |
|---------|---------|-----------------|
| CPU Threshold | 80% | `ApplicationInsightsProfiler_CpuThreshold` |
| Trigger Cooldown | 14400s (4h) | `ApplicationInsightsProfiler_Cpu_TriggerCooldownInSeconds` |
| Profiling Duration | 30s | `ApplicationInsightsProfiler_Cpu_ProfilingDurationInSeconds` |
| Enabled | true | `ApplicationInsightsProfiler_CpuTriggerEnabled` |

User-level overrides can also be set via code:

```csharp
builder.Services.AddServiceProfiler(options =>
{
    options.CPUTriggerThreshold = 60;  // Trigger at 60% CPU
    options.CPUTriggerCooldown = TimeSpan.FromMinutes(30);
});
```

## Diagnostic Logging

To observe the CPU monitoring pipeline, enable debug logging:

```json
{
  "Logging": {
    "LogLevel": {
      "Microsoft.ServiceProfiler": "Debug",
      "Microsoft.ApplicationInsights.Profiler": "Debug"
    }
  }
}
```

Or via environment variables (e.g., in Azure App Service):

```
Logging__LogLevel__Microsoft.ServiceProfiler=Debug
Logging__LogLevel__Microsoft.ApplicationInsights.Profiler=Debug
```

Key log messages to look for:
- `"CPU sample: X%"` or `"CPU sample (cgroup): X%"` — raw 1-second samples
- `"Effective CPU count from cgroup v2 quota: N"` or `"Effective CPU count from cgroup v1 quota: N"` — container CPU limit detected
- `"Using cgroup v2 CPU metrics from ..."` or `"Using cgroup v1 CPU metrics from ..."` — cgroup version detected
- `"No cgroup CPU stats found. CPU monitoring will report 0."` — cgroup files not available
- `"Baseline update: metric=X, old=Y, new=Z"` — 30-second rolling average updates
- `"Getting current CPU usage: X"` — the value the scheduling policy sees
- `"CPUMonitoringSchedulingPolicy request delay for ... StartProfilingSession"` — threshold exceeded, profiling triggered
