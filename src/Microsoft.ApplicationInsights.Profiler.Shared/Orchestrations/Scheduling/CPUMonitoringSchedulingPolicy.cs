//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using Microsoft.ServiceProfiler.DataContract.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ServiceProfiler.Orchestration;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights.Profiler.Shared.Contracts;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Orchestrations;

/// <summary>
/// Scheduling policy that will start profiling when recent average CPU usage exceeds a threshold
/// </summary>
internal sealed class CPUMonitoringSchedulingPolicy : EventPipeSchedulingPolicy
{
    private float _cpuThreshold;

    public CPUMonitoringSchedulingPolicy(
        IOptions<UserConfigurationBase> userConfiguration,
        ProfilerSettings profilerSettings,
        ProcessExpirationPolicy expirationPolicy,
        IDelaySource delaySource,
        IResourceUsageSource resourceUsageSource,
        IAgentStatusService agentStatusService,
        ILogger<CPUMonitoringSchedulingPolicy> logger
    ) : base(
        userConfiguration.Value.Duration,
        userConfiguration.Value.CPUTriggerCooldown,
        userConfiguration.Value.ConfigurationUpdateFrequency,
        profilerSettings,
        delaySource,
        expirationPolicy,
        resourceUsageSource,
        agentStatusService,
        logger
    )
    {
        _cpuThreshold = profilerSettings.CpuTriggerSettings.CpuThreshold;
    }

    public override string Source => nameof(CPUMonitoringSchedulingPolicy);

    // Return action + action duration based on whether recent average CPU usage exceeds threshold
    public override Task<IEnumerable<(TimeSpan duration, ProfilerAction action)>> GetScheduleAsync()
    {
        float cpuUsage = ResourceUsageSource.GetAverageCPUUsage();
        Logger.LogTrace("CPU Usage: {0}", cpuUsage);

        if (cpuUsage > _cpuThreshold)
        {
            return Task.FromResult(CreateProfilingSchedule(ProfilingDuration));
        }

        return Task.FromResult(CreateStandbySchedule());
    }

    protected override bool PolicyNeedsRefresh()
    {
        bool generalPolicyNeedsRefresh = base.PolicyNeedsRefresh();

        bool needsRefresh = false;
        CpuTriggerSettings cpuSettings = ProfilerSettings.CpuTriggerSettings;

        PolicyEnabled = UpdateRefreshAndGetSetting(cpuSettings.Enabled, PolicyEnabled, ref needsRefresh);
        ProfilingDuration = UpdateRefreshAndGetSetting(TimeSpan.FromSeconds(cpuSettings.CpuTriggerProfilingDurationInSeconds), ProfilingDuration, ref needsRefresh);
        ProfilingCooldown = UpdateRefreshAndGetSetting(TimeSpan.FromSeconds(cpuSettings.CpuTriggerCooldownInSeconds), ProfilingCooldown, ref needsRefresh);
        _cpuThreshold = UpdateRefreshAndGetSetting(cpuSettings.CpuThreshold, _cpuThreshold, ref needsRefresh);

        Logger.LogDebug("Policy needs refresh by {policy}: {decision}", nameof(CPUMonitoringSchedulingPolicy), needsRefresh);

        // Either the base policy needs refresh or any of the CPU sampling settings changed.
        return generalPolicyNeedsRefresh || needsRefresh;
    }
}

