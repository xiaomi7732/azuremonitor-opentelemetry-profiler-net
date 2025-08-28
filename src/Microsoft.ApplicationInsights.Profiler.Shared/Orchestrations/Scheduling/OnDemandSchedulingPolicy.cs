//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using Microsoft.ApplicationInsights.Profiler.Shared.Contracts;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ServiceProfiler.Orchestration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Orchestrations;

internal sealed class OnDemandSchedulingPolicy : EventPipeSchedulingPolicy
{
    private readonly IProfilerSettingsService _profilerSettingsService;
    private string? _evaluatedCollectionPlan;
    private const int DefaultProfileNowDurationInSeconds = 120;

    /// <summary>
    /// Scheduling policy that will start profiling when the Azure Portal "Start Profiling" button is clicked
    /// </summary>
    public OnDemandSchedulingPolicy(
        IOptions<UserConfigurationBase> userConfiguration,
        ProfilerSettings profilerSettings,
        ProcessExpirationPolicy expirationPolicy,
        IProfilerSettingsService profilerSettingsService,
        IDelaySource delaySource,
        IResourceUsageSource resourceUsageSource,
        IAgentStatusService agentStatusService,
        ILogger<OnDemandSchedulingPolicy> logger
    ) : base(
        userConfiguration.Value.Duration,
        TimeSpan.Zero,
        userConfiguration.Value.ConfigurationUpdateFrequency,
        profilerSettings,
        delaySource,
        expirationPolicy,
        resourceUsageSource,
        agentStatusService,
        logger
    )
    {
        _profilerSettingsService = profilerSettingsService ?? throw new ArgumentNullException(nameof(profilerSettingsService));
    }

    public override string Source => nameof(OnDemandSchedulingPolicy);

    public override async IAsyncEnumerable<(TimeSpan duration, ProfilerAction action)> GetScheduleAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Logger.LogTrace("Start to wait for profiler settings service to be ready.");

        // This is best effort.
        await _profilerSettingsService.WaitForInitializedAsync(RemoteSettingsServiceBase.DefaultInitializationTimeout, cancellationToken).ConfigureAwait(false);
        Logger.LogTrace("Profiler settings service is ready.");

        // A new collection plan is in place.
        if (!string.IsNullOrEmpty(ProfilerSettings.CollectionPlan) &&
            !string.Equals(ProfilerSettings.CollectionPlan, _evaluatedCollectionPlan, StringComparison.Ordinal))
        {
            _evaluatedCollectionPlan = ProfilerSettings.CollectionPlan;

            if (DateTime.Compare(DateTime.FromBinary(ProfilerSettings.Engine.Expiration).ToUniversalTime(), DateTime.UtcNow) >= 0)
            {
                int durationInSeconds = ProfilerSettings.Engine.ImmediateOptions.ProfilingDurationInSeconds;
                if (durationInSeconds <= 0)
                {
                    durationInSeconds = (int)ProfilingDuration.TotalSeconds;
                }

                // Client Safety. Notice, this shall not replace any server side check.
                if (durationInSeconds <= 0 || durationInSeconds > 360)
                {
                    Logger.LogWarning("Invalid Profile Now duration ({duration}s) detected. Use the default value of {defaultDelay}s instead.", durationInSeconds, DefaultProfileNowDurationInSeconds);
                    durationInSeconds = DefaultProfileNowDurationInSeconds;
                }

                await foreach ((TimeSpan, ProfilerAction) item in CreateProfilingSchedule(TimeSpan.FromSeconds(durationInSeconds)).ToAsyncEnumerable())
                {
                    yield return item;
                }
            }
        }

        await foreach ((TimeSpan, ProfilerAction) item in CreateStandbySchedule().ToAsyncEnumerable())
        {
            yield return item;
        }
    }
}
