using Microsoft.ApplicationInsights.Profiler.Shared.Contracts.CustomEvents;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.Extensions.Logging;

namespace Azure.Monitor.OpenTelemetry.Profiler.Core.Services;

internal class AgentStatusSender : IAgentStatusSender
{
    private readonly ILogger<AgentStatusSender> _logger;

    public AgentStatusSender(ILogger<AgentStatusSender> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task SendAsync(ProfilerAgentStatus agentStatus, string reason, CancellationToken cancellationToken)
    {
        if (agentStatus is null)
        {
            throw new ArgumentNullException(nameof(agentStatus));
        }

        if (string.IsNullOrEmpty(reason))
        {
            throw new ArgumentException($"'{nameof(reason)}' cannot be null or empty.", nameof(reason));
        }

        // Consider using a structured logging approach for better performance and readability. It's currently blocked by a compile error due to this bug: https://github.com/dotnet/extensions/issues/6733.
        _logger.LogInformation(ProfilerAgentStatus.TraceTelemetryFormat, ProfilerAgentStatus.EventName, agentStatus.Status, agentStatus.RoleInstance, reason);
        return Task.CompletedTask;
    }
}