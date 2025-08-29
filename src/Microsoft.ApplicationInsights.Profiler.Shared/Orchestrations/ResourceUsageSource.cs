//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using Microsoft.ApplicationInsights.Profiler.Shared.Contracts;
using Microsoft.ApplicationInsights.Profiler.Shared.Services;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ServiceProfiler.Contract.Agent.Profiler;
using Microsoft.ServiceProfiler.DataContract.Settings;
using Microsoft.ServiceProfiler.Orchestration;
using Microsoft.ServiceProfiler.Orchestration.MetricsProviders;
using Microsoft.ServiceProfiler.ProcessMonitor;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Orchestrations;

internal sealed class ResourceUsageSource : IResourceUsageSource
{
    private bool _running = false;

    private float _currentCPUBaseline = 0f;
    private float _currentMemoryBaseline = 0f;
    private bool _disposed = false;

    private BaselineTracker? _cpuBaselineTracker;
    private BaselineTracker? _memoryBaselineTracker;
    private readonly UserConfigurationBase _userConfigurations;
    private readonly IMetricsProvider _cpuMetricsProvider;
    private readonly IMetricsProvider _memoryMetricsProvider;
    private readonly CpuTriggerSettings _cpuTriggerSettings;
    private readonly MemoryTriggerSettings _memoryTriggerSettings;
    private readonly AgentStatusService _agentStatusService;
    private readonly ILogger _logger;
    private readonly ILoggerFactory _loggerFactory;

    private readonly SemaphoreSlim _statusChangeSemaphore = new SemaphoreSlim(1, 1);

    ///<summary>
    /// Aggregates CPU and RAM usage in recent times.
    ///</summary>
    public ResourceUsageSource(
        [FromKeyedServices(MetricsProviderCategory.CPU)]
        IMetricsProvider cpuMetricsProvider,
        [FromKeyedServices(MetricsProviderCategory.Memory)]
        IMetricsProvider memoryMetricsProvider,
        CpuTriggerSettings cpuTriggerSettings,
        MemoryTriggerSettings memoryTriggerSettings,
        ISerializationProvider serializer,
        IOptions<UserConfigurationBase> userConfigurations,
        AgentStatusService agentStatusService,
        ILogger<ResourceUsageSource> logger,
        ILoggerFactory loggerFactory)
    {
        _cpuMetricsProvider = cpuMetricsProvider;
        _memoryMetricsProvider = memoryMetricsProvider;
        _cpuTriggerSettings = cpuTriggerSettings;
        _memoryTriggerSettings = memoryTriggerSettings;
        _agentStatusService = agentStatusService ?? throw new ArgumentNullException(nameof(agentStatusService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));

        _userConfigurations = userConfigurations?.Value ?? throw new ArgumentNullException(nameof(userConfigurations));

        if (cpuMetricsProvider is null)
        {
            throw new ArgumentNullException(nameof(cpuMetricsProvider));
        }

        if (memoryMetricsProvider is null)
        {
            throw new ArgumentNullException(nameof(memoryMetricsProvider));
        }

        if (serializer is null)
        {
            throw new ArgumentNullException(nameof(serializer));
        }

        if (cpuTriggerSettings is null)
        {
            throw new ArgumentNullException(nameof(cpuTriggerSettings));
        }
        else
        {
            if (serializer.TrySerialize(cpuTriggerSettings, out string? serializedCpuTriggerSettings))
            {
                _logger.LogDebug("CPU Triggering settings: {settings}", serializedCpuTriggerSettings);
            }
        }

        if (memoryTriggerSettings is null)
        {
            throw new ArgumentNullException(nameof(memoryTriggerSettings));
        }
        else
        {
            if (serializer.TrySerialize(memoryTriggerSettings, out string? serializedMemoryTriggerSettings))
            {
                _logger.LogDebug("Memory Triggering settings: {settings}", serializedMemoryTriggerSettings);
            }
        }

        if (_userConfigurations.IsDisabled)
        {
            _logger.LogDebug("Resource usage monitoring is disabled because profiler is disabled.");
            return;
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        AgentStatus currentStatus = await _agentStatusService.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await OnAgentStatusChanged(currentStatus, "Initial status").ConfigureAwait(false);
        _agentStatusService.StatusChanged += OnAgentStatusChanged;
    }

    public float GetAverageCPUUsage()
        => LogAndReturn(() => _currentCPUBaseline, MetricsProviderCategory.CPU);

    public float GetAverageMemoryUsage()
        => LogAndReturn(() => _currentMemoryBaseline, MetricsProviderCategory.Memory);

    private async Task OnAgentStatusChanged(AgentStatus status, string reason)
    {
        await _statusChangeSemaphore.WaitAsync().ConfigureAwait(false);

        try
        {
            _cpuBaselineTracker?.Dispose();
            _memoryBaselineTracker?.Dispose();

            switch (status)
            {
                case AgentStatus.Active:
                    if (_running)
                    {
                        _logger.LogDebug("Resource usage monitoring already running.");
                        return; // already active
                    }
                    // Start or resume the baseline trackers.
                    _cpuBaselineTracker = CreateAndStartCPUBaselineTracker(_cpuTriggerSettings, _cpuMetricsProvider);
                    _memoryBaselineTracker = CreateAndStartMemoryBaselineTracker(_memoryMetricsProvider, _memoryTriggerSettings);
                    _running = true;
                    _logger.LogDebug("Resource usage monitoring started or resumed because agent status changed to Active for the reason of {reason}.", reason);
                    break;

                case AgentStatus.Inactive:
                    if (!_running)
                    {
                        _logger.LogDebug("Resource usage monitoring already stopped.");
                        return;  // already inactive
                    }
                    // Dispose of the trackers happened above.
                    _running = false;
                    _logger.LogDebug("Resource usage monitoring paused because agent status changed to Inactive for the reason of {reason}.", reason);
                    break;

                default:
                    throw new NotSupportedException($"Unsupported agent status: {status}");
            }
        }
        finally
        {
            _statusChangeSemaphore.Release();
        }
    }

    private BaselineTracker CreateAndStartMemoryBaselineTracker(IMetricsProvider memoryMetricsProvider, MemoryTriggerSettings memoryTriggerSettings)
    {
        // TODO: Fix with ActivatorUtilities
        BaselineTracker memoryBaselineTracker = new(
            new RollingHistoryArray<float>(
                TimeSpan.FromMinutes(memoryTriggerSettings.MemoryRollingHistorySize),
                TimeSpan.FromSeconds(memoryTriggerSettings.MemoryRollingHistoryInterval)),
                TimeSpan.FromSeconds(memoryTriggerSettings.MemoryAverageWindow), memoryMetricsProvider.GetNextValue, _loggerFactory.CreateLogger<BaselineTracker>());

        memoryBaselineTracker.RegisterCallback((oldBaseline, newBaseline) =>
        {
            _logger.LogTrace("Memory monitoring calling back. Old base: {old}, new base: {new}", oldBaseline, newBaseline);
            _currentMemoryBaseline = newBaseline;
        });

        memoryBaselineTracker.Start();

        return memoryBaselineTracker;
    }

    private BaselineTracker CreateAndStartCPUBaselineTracker(CpuTriggerSettings cpuTriggerSettings, IMetricsProvider cpuMetricsProvider)
    {
        BaselineTracker cpuBaselineTracker = new(
            new RollingHistoryArray<float>(
                TimeSpan.FromMinutes(cpuTriggerSettings.CpuRollingHistorySize),
                TimeSpan.FromSeconds(cpuTriggerSettings.CpuRollingHistoryInterval)),
            TimeSpan.FromSeconds(cpuTriggerSettings.CpuAverageWindow),
            getNextMetric: cpuMetricsProvider.GetNextValue,
            logger: _loggerFactory.CreateLogger<BaselineTracker>());

        cpuBaselineTracker.RegisterCallback((oldBaseline, newBaseline) =>
        {
            _logger.LogTrace("CPU monitoring calling back. Old base: {old}, new base: {new}", oldBaseline, newBaseline);
            _currentCPUBaseline = newBaseline;
        });

        cpuBaselineTracker.Start();
        return cpuBaselineTracker;
    }

    private float LogAndReturn(Func<float> valueProvider, MetricsProviderCategory category)
    {
        float value = valueProvider();
        _logger.LogDebug("Getting current {category} usage: {value}", category, value);
        return value;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _cpuBaselineTracker?.Dispose();
            _memoryBaselineTracker?.Dispose();
            _agentStatusService.StatusChanged -= OnAgentStatusChanged;
            _statusChangeSemaphore.Dispose();
            _disposed = true;
        }
    }
}
