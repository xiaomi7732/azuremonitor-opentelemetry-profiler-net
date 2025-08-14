using Microsoft.ApplicationInsights.Profiler.Shared.Contracts.CustomEvents;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;

internal interface IAgentStatusService
{
    ValueTask<ProfilerAgentStatus> InitializeAsync(CancellationToken cancellationToken);
}
