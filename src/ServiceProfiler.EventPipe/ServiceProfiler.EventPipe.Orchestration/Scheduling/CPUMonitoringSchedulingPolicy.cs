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
    /// <summary>
    /// Scheduling policy that will start profiling when recent average CPU usage exceeds a threshold
    /// </summary>
    internal sealed class CPUMonitoringSchedulingPolicy : EventPipeSchedulingPolicy
    {
        private float _cpuThreshold;

        public CPUMonitoringSchedulingPolicy(
            IOptions<UserConfiguration> userConfiguration,
            ProfilerSettings profilerSettings,
            IExpirationPolicy expirationPolicy,
            IDelaySource delaySource,
            IResourceUsageSource resourceUsageSource,
            ILogger<CPUMonitoringSchedulingPolicy> logger
        ) : base(
            userConfiguration.Value.Duration,
            userConfiguration.Value.CPUTriggerCooldown,
            userConfiguration.Value.ConfigurationUpdateFrequency,
            profilerSettings,
            delaySource,
            expirationPolicy,
            resourceUsageSource,
            logger
        )
        {
            _cpuThreshold = profilerSettings.CpuTriggerSettings.CpuThreshold;
        }

        public override string Source => nameof(CPUMonitoringSchedulingPolicy);

        // Return action + action duration based on whether recent average CPU usage exceeds threshold
        public override Task<IEnumerable<(TimeSpan duration, ProfilerAction action)>> GetScheduleAsync()
        {
            var cpuUsage = ResourceUsageSource.GetAverageCPUUsage();
            Logger.LogTrace("CPU Usage: {0}", cpuUsage);

            List<(TimeSpan, ProfilerAction)> result = new List<(TimeSpan, ProfilerAction)>();
            if (cpuUsage > _cpuThreshold)
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
            CpuTriggerSettings cpuSettings = ProfilerSettings.CpuTriggerSettings;

            ProfilerEnabled = UpdateRefreshAndGetSetting<bool>(ProfilerSettings.Enabled, ProfilerEnabled, ref needsRefresh);
            PolicyEnabled = UpdateRefreshAndGetSetting<bool>(cpuSettings.Enabled, PolicyEnabled, ref needsRefresh);
            ProfilingDuration = UpdateRefreshAndGetSetting<TimeSpan>(TimeSpan.FromSeconds(cpuSettings.CpuTriggerProfilingDurationInSeconds), ProfilingDuration, ref needsRefresh);
            ProfilingCooldown = UpdateRefreshAndGetSetting<TimeSpan>(TimeSpan.FromSeconds(cpuSettings.CpuTriggerCooldownInSeconds), ProfilingCooldown, ref needsRefresh);
            _cpuThreshold = UpdateRefreshAndGetSetting<float>(cpuSettings.CpuThreshold, _cpuThreshold, ref needsRefresh);

            return needsRefresh;
        }
    }
}
