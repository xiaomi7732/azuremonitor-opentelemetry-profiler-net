//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using System;
using Microsoft.ApplicationInsights.Profiler.Core.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.ServiceProfiler.DataContract.Settings;
using Microsoft.ServiceProfiler.Orchestration;
using Microsoft.ServiceProfiler.Orchestration.MetricsProviders;
using Microsoft.ServiceProfiler.ProcessMonitor;

namespace Microsoft.ApplicationInsights.Profiler.Core.Orchestration;

internal sealed class ResourceUsageSource : IResourceUsageSource
{
    ///<summary>
    /// Aggregates CPU and RAM usage in recent times.
    ///</summary>
    public ResourceUsageSource(
        IMetricsProviderResolver<MetricsProviderCategory> metricsProviderResolver,
        CpuTriggerSettings cpuTriggerSettings,
        MemoryTriggerSettings memoryTriggerSettings,
        ISerializationProvider serializer,
        ILogger<ResourceUsageSource> logger)
    {
        if (serializer is null)
        {
            throw new ArgumentNullException(nameof(serializer));
        }

        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (metricsProviderResolver is null)
        {
            throw new ArgumentNullException(nameof(metricsProviderResolver));
        }

        IMetricsProvider cpuMetricsProvider = metricsProviderResolver.Resolve(MetricsProviderCategory.CPU) ?? throw new InvalidOperationException("Can't resolve metrics provider.");
        IMetricsProvider memoryMetricsProvider = metricsProviderResolver.Resolve(MetricsProviderCategory.Memory) ?? throw new InvalidOperationException("Can't resolve metrics provider.");

        if (cpuTriggerSettings is null)
        {
            throw new ArgumentNullException(nameof(cpuTriggerSettings));
        }
        else if (_logger.IsEnabled(LogLevel.Debug))
        {
            if (serializer.TrySerialize(cpuTriggerSettings, out string serializedCpuTriggerSettings))
            {
                _logger.LogDebug("CPU Triggering settings: {0}", serializedCpuTriggerSettings);
            }
        }

        if (memoryTriggerSettings is null)
        {
            throw new ArgumentNullException(nameof(memoryTriggerSettings));
        }
        else if (_logger.IsEnabled(LogLevel.Debug))
        {
            if (serializer.TrySerialize(memoryTriggerSettings, out string serializedMemoryTriggerSettings))
            {
                _logger.LogDebug("Memory Triggering settings: {0}", serializedMemoryTriggerSettings);
            }
        }

        _cpuBaselineTracker = new BaselineTracker(
            new RollingHistoryArray<float>(
                TimeSpan.FromMinutes(cpuTriggerSettings.CpuRollingHistorySize),
                TimeSpan.FromSeconds(cpuTriggerSettings.CpuRollingHistoryInterval)),
            TimeSpan.FromSeconds(cpuTriggerSettings.CpuAverageWindow), cpuMetricsProvider.GetNextValue, NullLogger<BaselineTracker>.Instance);

        _memoryBaselineTracker = new BaselineTracker(
            new RollingHistoryArray<float>(
                TimeSpan.FromMinutes(memoryTriggerSettings.MemoryRollingHistorySize),
                TimeSpan.FromSeconds(memoryTriggerSettings.MemoryRollingHistoryInterval)),
                TimeSpan.FromSeconds(memoryTriggerSettings.MemoryAverageWindow), memoryMetricsProvider.GetNextValue, NullLogger<BaselineTracker>.Instance);

        _cpuBaselineTracker.RegisterCallback((oldBaseline, newBaseline) =>
        {
            _logger.LogTrace("CPU monitoring calling back. Old base: {0}, new base: {1}", oldBaseline, newBaseline);
            _currentCPUBaseline = newBaseline;
        });

        _memoryBaselineTracker.RegisterCallback((oldBaseline, newBaseline) =>
        {
            _logger.LogTrace("Memory monitoring calling back. Old base: {0}, new base: {1}", oldBaseline, newBaseline);
            _currentMemoryBaseline = newBaseline;
        });

        _cpuBaselineTracker.Start();
        _memoryBaselineTracker.Start();
    }

    public float GetAverageCPUUsage() => _currentCPUBaseline;
    public float GetAverageMemoryUsage() => _currentMemoryBaseline;

    public void Dispose()
    {
        if (!_disposed)
        {
            if (_cpuBaselineTracker != null)
            {
                _cpuBaselineTracker.Dispose();
                _cpuBaselineTracker = null;
            }

            if (_memoryBaselineTracker != null)
            {
                _memoryBaselineTracker.Dispose();
                _memoryBaselineTracker = null;
            }

            _disposed = true;
        }
    }

    private BaselineTracker _cpuBaselineTracker;
    private BaselineTracker _memoryBaselineTracker;
    private float _currentCPUBaseline = 0f;
    private float _currentMemoryBaseline = 0f;
    private readonly ILogger _logger;
    private bool _disposed = false;
}
