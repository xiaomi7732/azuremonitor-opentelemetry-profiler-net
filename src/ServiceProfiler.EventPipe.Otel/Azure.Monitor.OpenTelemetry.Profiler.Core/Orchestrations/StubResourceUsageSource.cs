//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

// TODO: Consider moving this to the the process monitoring project.

using Microsoft.Extensions.Logging;
using Microsoft.ServiceProfiler.Orchestration;

namespace Azure.Monitor.OpenTelemetry.Profiler.Core.Orchestrations;
public sealed class StubResourceUsageSource : IResourceUsageSource
{
    private readonly ILogger _logger;

    ///<summary>
    /// Aggregates CPU and RAM usage in recent times.
    ///</summary>
    public StubResourceUsageSource(ILogger<StubResourceUsageSource> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void Dispose()
    {
    }

    public float GetAverageCPUUsage()
    {
        _logger.LogInformation("{name} triggered in {className}", nameof(GetAverageCPUUsage), nameof(StubResourceUsageSource));
        return 0;
    }

    public float GetAverageMemoryUsage()
    {
        _logger.LogInformation("{name} triggered in {className}", nameof(GetAverageMemoryUsage), nameof(StubResourceUsageSource));
        return 0;
    }
}
