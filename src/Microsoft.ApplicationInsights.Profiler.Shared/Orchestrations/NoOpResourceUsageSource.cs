// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.ServiceProfiler.Orchestration;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Orchestrations;

/// <summary>
/// A no-op resource usage source for unsupported platforms.
/// Returns zero for all metrics and performs no monitoring.
/// </summary>
internal sealed class NoOpResourceUsageSource : IResourceUsageSource
{
    public float GetAverageCPUUsage() => 0f;

    public float GetAverageMemoryUsage() => 0f;

    public void Dispose()
    {
        // No resources to release.
    }
}
