//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using Microsoft.ApplicationInsights.Profiler.Shared.Contracts;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ServiceProfiler.Orchestration;
using Microsoft.ServiceProfiler.Orchestration.Modes;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Orchestrations;

/// <summary>
/// Scheduling policy that uses a coin-flip mechanism each cycle: with probability equal to
/// the configured sampling rate a profiling session is started, otherwise the profiler
/// stands by for a configured standby duration before the next coin flip.
/// </summary>
internal sealed class RandomSchedulingPolicy : EventPipeSchedulingPolicy
{
    private readonly IRandomSource _randomSource;

    private double _samplingRate;
    private TimeSpan _standbyDuration;

    public RandomSchedulingPolicy(
        IOptions<UserConfigurationBase> userConfiguration,
        ProfilerSettings profilerSettings,
        ProcessExpirationPolicy expirationPolicy,
        IDelaySource delaySource,
        IRandomSource randomSource,
        IResourceUsageSource resourceUsageSource,
        IAgentStatusService agentStatusService,
        ILogger<RandomSchedulingPolicy> logger)
        : base(
            profilingDuration: TimeSpan.FromSeconds(profilerSettings.SamplingOptions.ProfilingDurationInSeconds),
            profilingCooldown: TimeSpan.Zero,
            pollingInterval: userConfiguration.Value.ConfigurationUpdateFrequency,
            profilerSettings: profilerSettings,
            delaySource,
            expirationPolicy,
            resourceUsageSource,
            agentStatusService,
            logger
        )
    {
        PolicyEnabled = profilerSettings.SamplingOptions.Enabled;
        _randomSource = randomSource ?? throw new ArgumentNullException(nameof(randomSource));
        _samplingRate = profilerSettings.SamplingOptions.SamplingRate;
        _standbyDuration = TimeSpan.FromSeconds(profilerSettings.SamplingOptions.StandbyDurationInSeconds);
    }

    public override async IAsyncEnumerable<(TimeSpan duration, ProfilerAction action)> GetScheduleAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        double clampedRate = Math.Clamp(_samplingRate, 0, 1);
        if (clampedRate != _samplingRate)
        {
            Logger.LogWarning("SamplingRate {samplingRate} is outside the valid range [0, 1]. Clamping to {clampedRate}.", _samplingRate, clampedRate);
        }

        // Ensure standby is never zero/negative to prevent a tight spin loop.
        TimeSpan effectiveStandby = _standbyDuration > TimeSpan.Zero ? _standbyDuration : PollingInterval;
        if (effectiveStandby != _standbyDuration)
        {
            Logger.LogWarning("StandbyDuration {standby} is not positive. Falling back to PollingInterval {polling}.", _standbyDuration, PollingInterval);
        }

        if (ProfilingDuration.TotalSeconds <= 0)
        {
            Logger.LogWarning("ProfilingDuration {duration} is not positive. Falling back to standby.", ProfilingDuration);
            yield return (effectiveStandby, ProfilerAction.Standby);
            yield break;
        }

        if (_randomSource.NextDouble() < clampedRate)
        {
            Logger.LogDebug("Coin flip succeeded (rate={samplingRate:P}). Starting profiling session for {duration}.", clampedRate, ProfilingDuration);
            yield return (ProfilingDuration, ProfilerAction.StartProfilingSession);

            cancellationToken.ThrowIfCancellationRequested();
            yield return (ProfilingCooldown, ProfilerAction.Standby);
        }
        else
        {
            Logger.LogDebug("Coin flip missed (rate={samplingRate:P}). Standing by for {standby}.", clampedRate, effectiveStandby);
            yield return (effectiveStandby, ProfilerAction.Standby);
        }
    }

    protected override bool PolicyNeedsRefresh()
    {
        bool generalPolicyNeedsRefresh = base.PolicyNeedsRefresh();

        bool needsRefresh = false;
        SamplingOptions samplingSettings = ProfilerSettings.SamplingOptions;

        PolicyEnabled = UpdateRefreshAndGetSetting(samplingSettings.Enabled, PolicyEnabled, ref needsRefresh);
        ProfilingDuration = UpdateRefreshAndGetSetting(TimeSpan.FromSeconds(samplingSettings.ProfilingDurationInSeconds), ProfilingDuration, ref needsRefresh);
        _samplingRate = UpdateRefreshAndGetSetting(samplingSettings.SamplingRate, _samplingRate, ref needsRefresh);
        _standbyDuration = UpdateRefreshAndGetSetting(TimeSpan.FromSeconds(samplingSettings.StandbyDurationInSeconds), _standbyDuration, ref needsRefresh);

        // Either the base policy needs refresh or any of the random sampling settings changed.
        return generalPolicyNeedsRefresh || needsRefresh;
    }

    public override string Source { get; } = nameof(RandomSchedulingPolicy);
}
