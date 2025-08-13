using Microsoft.ApplicationInsights.Profiler.Shared.Contracts.CustomEvents;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.Extensions.Logging;

namespace Microsoft.ServiceProfiler.Contract.Agent;

internal class AgentStatusSender : IAgentStatusSender
{
    private readonly ILogger<AgentStatusSender> _logger;

    public AgentStatusSender(ILogger<AgentStatusSender> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task SendAsync(ProfilerAgentStatus agentStatus, CancellationToken cancellationToken)
    {
        _logger.LogInformation("{microsoft.custom_event.name} {status} {instance}", "ProfilerStatus", agentStatus.Status, agentStatus.RoleInstance);
        return Task.CompletedTask;
    }
}