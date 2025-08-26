using Microsoft.ServiceProfiler.Contract.Agent.Profiler;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;

internal interface IAgentStatusService
{
    ValueTask<AgentStatus> InitializeAsync(CancellationToken cancellationToken);
}
