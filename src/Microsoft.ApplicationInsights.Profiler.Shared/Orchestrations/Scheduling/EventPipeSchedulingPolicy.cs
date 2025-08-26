using System;
using Microsoft.ServiceProfiler.Orchestration;
using Microsoft.Extensions.Logging;
using Microsoft.ApplicationInsights.Profiler.Shared.Contracts;
using System.Collections.Generic;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.ServiceProfiler.Contract.Agent.Profiler;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Orchestrations;

internal abstract class EventPipeSchedulingPolicy : SchedulingPolicy
{
    private readonly IAgentStatusService _agentStatusService;

    /// <summary>
    /// The expected new status for the scheduler to run into.
    /// Null if no change is requested.
    /// </summary>
    private AgentStatus? _agentStatusRequest = null;

    public EventPipeSchedulingPolicy(
        TimeSpan profilingDuration,
        TimeSpan profilingCooldown,
        TimeSpan pollingInterval,
        ProfilerSettings profilerSettings,
        IDelaySource delaySource,
        IExpirationPolicy expirationPolicy,
        IResourceUsageSource resourceUsageSource,
        IAgentStatusService agentStatusService,
        ILogger<EventPipeSchedulingPolicy> logger
    ) : base(profilingDuration, profilingCooldown, pollingInterval, delaySource, expirationPolicy, logger)
    {
        ProfilerSettings = profilerSettings;
        ResourceUsageSource = resourceUsageSource;
        _agentStatusService = agentStatusService ?? throw new ArgumentNullException(nameof(agentStatusService));
        _agentStatusService.StatusChanged += OnAgentStatusChanged;
    }

    private void OnAgentStatusChanged(AgentStatus status, string reason) => _agentStatusRequest = status;

    protected T UpdateRefreshAndGetSetting<T>(T newSetting, T currentSetting, ref bool needsRefresh)
    {
        needsRefresh = needsRefresh || currentSetting is not null && !currentSetting.Equals(newSetting);

        return newSetting;
    }

    protected override bool PolicyNeedsRefresh()
    {
        // If there is a new status, we need to refresh the policy.
        bool isEnabled = _agentStatusRequest is not null && _agentStatusRequest == AgentStatus.Active;
        // Reset
        _agentStatusRequest = null;

        // Backward compatibility: if the profiler settings is disabled, we should not profile regardless of the agent status.
        isEnabled = isEnabled && ProfilerSettings.Enabled;

        // Policy needs refresh if the enabled status has changed.
        if (isEnabled != ProfilerEnabled)
        {
            ProfilerEnabled = ProfilerSettings.Enabled;
            return true;
        }

        return false;
    }

    protected ProfilerSettings ProfilerSettings { get; }
    protected IResourceUsageSource ResourceUsageSource { get; }

    protected IEnumerable<(TimeSpan duration, ProfilerAction action)> CreateProfilingSchedule(TimeSpan profilingDuration)
    {
        yield return (profilingDuration, ProfilerAction.StartProfilingSession);
        yield return (ProfilingCooldown, ProfilerAction.Standby);
    }

    protected IEnumerable<(TimeSpan duration, ProfilerAction action)> CreateStandbySchedule()
    {
        yield return (PollingInterval, ProfilerAction.Standby);
    }
}