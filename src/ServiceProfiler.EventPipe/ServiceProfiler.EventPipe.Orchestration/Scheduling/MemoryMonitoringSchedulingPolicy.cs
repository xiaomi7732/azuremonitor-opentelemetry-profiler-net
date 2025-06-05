//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using Microsoft.ApplicationInsights.Profiler.Core.Contracts;
using Microsoft.ServiceProfiler.DataContract.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ServiceProfiler.Orchestration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.ApplicationInsights.Profiler.Core.Orchestration
{
    internal sealed class MemoryMonitoringSchedulingPolicy : EventPipeSchedulingPolicy
    {
        private float _memoryThreshold;

        /// <summary>
        /// Scheduling policy that will start profiling when recent average RAM usage exceeds a threshold
        /// </summary>
        public MemoryMonitoringSchedulingPolicy(
            IOptions<UserConfiguration> userConfiguration,
            ProfilerSettings profilerSettings,
            IExpirationPolicy expirationPolicy,
            IDelaySource delaySource,
            IResourceUsageSource resourceUsageSource,
            ILogger<MemoryMonitoringSchedulingPolicy> logger
        ) : base(
            userConfiguration.Value.Duration,
            userConfiguration.Value.MemoryTriggerCooldown,
            userConfiguration.Value.ConfigurationUpdateFrequency,
            profilerSettings,
            delaySource,
            expirationPolicy,
            resourceUsageSource,
            logger
        )
        {
            _memoryThreshold = profilerSettings.MemoryTriggerSettings.MemoryThreshold;
        }

        public override string Source => nameof(MemoryMonitoringSchedulingPolicy);

        // Return action + action duration based on whether recent average RAM usage exceeds threshold
        public override Task<IEnumerable<(TimeSpan duration, ProfilerAction action)>> GetScheduleAsync()
        {
            float memoryUsage = ResourceUsageSource.GetAverageMemoryUsage();
            Logger.LogTrace("Memory Usage: {0}", memoryUsage);

            List<(TimeSpan, ProfilerAction)> result = new List<(TimeSpan, ProfilerAction)>();

            if (memoryUsage > _memoryThreshold)
            {
                result.Add((ProfilingDuration, ProfilerAction.StartProfilingSession));
                result.Add((ProfilingCooldown, ProfilerAction.Standby));
            }
            else
            {
                result.Add((PollingInterval, ProfilerAction.Standby));
            }

            return Task.FromResult(result.AsEnumerable());
        }

        protected override bool PolicyNeedsRefresh()
        {
            bool needsRefresh = false;
            MemoryTriggerSettings memorySettings = ProfilerSettings.MemoryTriggerSettings;

            ProfilerEnabled = UpdateRefreshAndGetSetting<bool>(ProfilerSettings.Enabled, ProfilerEnabled, ref needsRefresh);
            PolicyEnabled = UpdateRefreshAndGetSetting<bool>(memorySettings.Enabled, PolicyEnabled, ref needsRefresh);
            ProfilingDuration = UpdateRefreshAndGetSetting<TimeSpan>(TimeSpan.FromSeconds(memorySettings.MemoryTriggerProfilingDurationInSeconds), ProfilingDuration, ref needsRefresh);
            ProfilingCooldown = UpdateRefreshAndGetSetting<TimeSpan>(TimeSpan.FromSeconds(memorySettings.MemoryTriggerCooldownInSeconds), ProfilingCooldown, ref needsRefresh);
            _memoryThreshold = UpdateRefreshAndGetSetting<float>(memorySettings.MemoryThreshold, _memoryThreshold, ref needsRefresh);

            return needsRefresh;
        }
    }
}
