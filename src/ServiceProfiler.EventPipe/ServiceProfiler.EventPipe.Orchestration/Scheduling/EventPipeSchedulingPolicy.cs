using System;
using Microsoft.ServiceProfiler.Orchestration;
using Microsoft.ApplicationInsights.Profiler.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace Microsoft.ApplicationInsights.Profiler.Core.Orchestration;

internal abstract class EventPipeSchedulingPolicy : SchedulingPolicy
{
    public EventPipeSchedulingPolicy(
        TimeSpan profilingDuration,
        TimeSpan profilingCooldown,
        TimeSpan pollingInterval,
        ProfilerSettings profilerSettings,
        IDelaySource delaySource,
        IExpirationPolicy expirationPolicy,
        IResourceUsageSource resourceUsageSource,
        ILogger<EventPipeSchedulingPolicy> logger
    ) : base(profilingDuration, profilingCooldown, pollingInterval, delaySource, expirationPolicy, logger)
    {
        ProfilerSettings = profilerSettings;
        ResourceUsageSource = resourceUsageSource;
    }

    protected T UpdateRefreshAndGetSetting<T>(T newSetting, T currentSetting, ref bool needsRefresh)
    {
        needsRefresh = needsRefresh || !currentSetting.Equals(newSetting);

        return newSetting;
    }
    protected override bool PolicyNeedsRefresh()
    {
        if (ProfilerSettings.Enabled != ProfilerEnabled)
        {
            ProfilerEnabled = ProfilerSettings.Enabled;
            return true;
        }

        return false;
    }

    protected ProfilerSettings ProfilerSettings { get; }
    protected IResourceUsageSource ResourceUsageSource { get; }
}
