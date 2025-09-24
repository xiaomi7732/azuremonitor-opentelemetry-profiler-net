using Microsoft.ApplicationInsights.Profiler.Shared.Contracts.CustomEvents;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;

internal interface IAgentStatusSender
{
    Task SendAsync(ProfilerAgentStatus agentStatus, string reason, CancellationToken cancellationToken);
}