using Azure.Monitor.OpenTelemetry.Profiler.Core.Contracts;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Orchestrations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ServiceProfiler.Orchestration;

namespace Azure.Monitor.OpenTelemetry.Profiler.Core.Orchestrations;

internal class OneTimeSchedulingPolicy : EventPipeSchedulingPolicy
{
    public OneTimeSchedulingPolicy(
        IOptions<ServiceProfilerOptions> userConfiguration,
        ProfilerSettings profilerSettings,
        IDelaySource delaySource,
        IResourceUsageSource resourceUsageSource,
        LimitedExpirationPolicyFactory limitedExpirationPolicyFactory,
        ILogger<OneTimeSchedulingPolicy> logger
    ) : base(
        userConfiguration.Value.Duration,
        TimeSpan.FromHours(4),
        userConfiguration.Value.ConfigurationUpdateFrequency,
        profilerSettings,
        delaySource,
        limitedExpirationPolicyFactory.Create(1),
        resourceUsageSource,
        logger
    )
    { }

    public override string Source { get; } = nameof(OneTimeSchedulingPolicy);
    public override Task<IEnumerable<(TimeSpan duration, ProfilerAction action)>> GetScheduleAsync()
    {
        return Task.FromResult(new List<(TimeSpan, ProfilerAction)>()
            {
                (ProfilingDuration, ProfilerAction.StartProfilingSession),
                (ProfilingDuration, ProfilerAction.Standby), // The duration will be overwritten by the expiration policy.
            }.AsEnumerable());
    }

    protected override bool PolicyNeedsRefresh() => false;
}

