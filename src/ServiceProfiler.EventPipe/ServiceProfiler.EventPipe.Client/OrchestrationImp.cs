using Microsoft.ApplicationInsights.Profiler.Core.Contracts;
using Microsoft.ApplicationInsights.Profiler.Shared.Orchestrations;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ServiceProfiler.Orchestration;
using System.Collections.Generic;

namespace Microsoft.ApplicationInsights.Profiler.Core;

internal class OrchestrationImp : OrchestratorEventPipe
{
    public OrchestrationImp(
        IServiceProfilerProvider profilerProvider,
        IOptions<UserConfiguration> config, 
        IEnumerable<SchedulingPolicy> policyCollection, 
        IDelaySource delaySource,
        IAgentStatusService agentStatusService,
        IResourceUsageSource resourceUsageSource,
        ILogger<OrchestrationImp> logger) : base(profilerProvider, config, policyCollection, delaySource, agentStatusService, resourceUsageSource, logger)
    {
    }
}
