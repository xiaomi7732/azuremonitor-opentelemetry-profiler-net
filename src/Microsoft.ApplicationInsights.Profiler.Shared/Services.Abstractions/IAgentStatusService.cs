using Microsoft.ServiceProfiler.Contract.Agent.Profiler;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;

internal interface IAgentStatusService
{
    /// <summary>
    /// Event fired when the status of the agent changes.
    /// </summary>
    event Func<AgentStatus, string, Task>? StatusChanged;


    /// <summary>
    /// Gets the initial status of the agent. If not initialized, it will initialize the status first.
    /// This method is thread-safe, and can be called multiple times. The initialization will only happen once.
    /// If the status is already initialized, it will return the current status.
    /// </summary>
    ValueTask<AgentStatus> InitializeAsync(CancellationToken cancellationToken);
}
