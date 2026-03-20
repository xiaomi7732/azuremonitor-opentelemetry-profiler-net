# Memory Usage Monitoring

This document explains how the profiler monitors memory usage to trigger profiling sessions when memory exceeds a configured threshold.

## Overview

The memory monitoring pipeline follows the same three-layer architecture as [CPU monitoring](CpuUsageMonitoring.md):

1. **Metrics Reading** — reads current memory usage on demand
2. **Baseline Tracking** — aggregates readings into a rolling average every 30 seconds
3. **Scheduling Policy** — compares the average against the threshold every 5 seconds

```
Memory Reader (on-demand)
        ↓
Baseline Tracker (30s rolling average)
        ↓
Memory Monitoring Policy (5s polling)
        ↓ memoryUsage > threshold?
Start Profiling Session
```

## How Memory is Measured

### Windows

Uses the Win32 API to get total physical memory and `Process.WorkingSet64` for current process memory. Returns usage as:

```
Memory% = (processWorkingSet / totalPhysicalMemory) × 100
```

### Linux

Reads from `/proc/meminfo` to get system-wide total and available memory:

```
Memory% = (1 - MemAvailable / MemTotal) × 100
```

This measures system-wide memory pressure, not just the current process.

Unlike CPU sampling, memory readings are computed synchronously on demand — there is no separate sampling thread. Each time the baseline tracker requests a new value, a fresh reading is taken directly.

## Baseline Tracking (Sliding Window)

Memory readings are aggregated into a smoothed average using the same circular buffer mechanism as CPU monitoring.

### Configuration

| Setting | Default | Description |
|---------|---------|-------------|
| `MemoryRollingHistorySize` | 10 minutes | Total window size for the circular buffer |
| `MemoryRollingHistoryInterval` | 30 seconds | How often a reading is recorded into the buffer |
| `MemoryAverageWindow` | 30 seconds | Sliding window for computing the reported average |

### How it works

1. Every 30 seconds, the latest memory reading is recorded into a circular buffer (sized at `10min / 30s = 20 slots`).
2. The average is computed over the most recent 30 seconds of entries.
3. This average is what the scheduling policy uses for threshold comparison.

## Memory Monitoring Scheduling Policy

The policy checks the memory baseline every 5 seconds:

- If `memoryUsage > MemoryThreshold` → starts a profiling session, then enters a cooldown period.
- If `memoryUsage ≤ MemoryThreshold` → stands by for 5 seconds, then checks again.

### Configuration

These settings can be configured via app settings or environment variables:

| Setting | Default | App Setting Key |
|---------|---------|-----------------|
| Memory Threshold | 80% | `ApplicationInsightsProfiler_MemoryThreshold` |
| Trigger Cooldown | 14400s (4h) | `ApplicationInsightsProfiler_Memory_TriggerCooldownInSeconds` |
| Profiling Duration | 30s | `ApplicationInsightsProfiler_Memory_ProfilingDurationInSeconds` |
| Enabled | true | `ApplicationInsightsProfiler_MemoryTriggerEnabled` |

User-level overrides can also be set via code:

```csharp
builder.Services.AddServiceProfiler(options =>
{
    options.MemoryTriggerThreshold = 70;  // Trigger at 70% memory
    options.MemoryTriggerCooldown = TimeSpan.FromMinutes(30);
});
```

## Diagnostic Logging

To observe the memory monitoring pipeline, enable debug logging:

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
- `"Get memory usage: free/total: X/Y"` — raw readings from the provider
- `"Baseline update: metric=X, old=Y, new=Z"` — 30-second rolling average updates
- `"Getting current Memory usage: X"` — the value the scheduling policy sees
- `"MemoryMonitoringSchedulingPolicy request delay for ... StartProfilingSession"` — threshold exceeded, profiling triggered
