using System;
using Microsoft.ServiceProfiler.Orchestration;
using Microsoft.Extensions.Logging;
using Microsoft.ApplicationInsights.Profiler.Shared.Contracts;
using System.Collections.Generic;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.ServiceProfiler.Contract.Agent.Profiler;
using System.Threading.Tasks;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Orchestrations;

internal abstract class EventPipeSchedulingPolicy : SchedulingPolicy, IDisposable
{
    private readonly IAgentStatusService _agentStatusService;
    private bool _disposed; // dispose tracking

    /// <summary>
    /// The expected new status for the scheduler to run into.
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
        ProfilerSettings = profilerSettings ?? throw new ArgumentNullException(nameof(profilerSettings));
        ResourceUsageSource = resourceUsageSource ?? throw new ArgumentNullException(nameof(resourceUsageSource));
        _agentStatusService = agentStatusService ?? throw new ArgumentNullException(nameof(agentStatusService));
        _agentStatusService.StatusChanged += OnAgentStatusChanged;
    }

    private Task OnAgentStatusChanged(AgentStatus status, string reason)
    {
        _agentStatusRequest = status;
        return Task.CompletedTask;
    }
     

    protected T UpdateRefreshAndGetSetting<T>(T newSetting, T currentSetting, ref bool needsRefresh)
    {
        needsRefresh = needsRefresh || currentSetting is not null && !currentSetting.Equals(newSetting);

        return newSetting;
    }

    protected override bool PolicyNeedsRefresh()
    {
        bool toEnable;

        if (_agentStatusRequest is not null)
        {
            // If there's agent activation/deactivation request, follow it.
            Logger.LogDebug("Desired agent status: {status}", _agentStatusRequest);
            toEnable = _agentStatusRequest == AgentStatus.Active;
        }
        else
        {
            // Otherwise, follow the profiler settings for backward compatibility.
            toEnable = ProfilerSettings.Enabled;
        }

        // Policy needs refresh if the enabled status has changed.
        if (ProfilerEnabled != toEnable)
        {
            ProfilerEnabled = toEnable;
            Logger.LogDebug("Policy needs refresh by {policy}: true", nameof(EventPipeSchedulingPolicy));
            return true;
        }

        Logger.LogDebug("Policy needs refresh by {policy}: false", nameof(EventPipeSchedulingPolicy));
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

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (disposing)
        {
            _agentStatusService.StatusChanged -= OnAgentStatusChanged;
        }
        _disposed = true;
    }
}