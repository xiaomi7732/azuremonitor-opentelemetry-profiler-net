using Azure.Core;
using Microsoft.ApplicationInsights.Profiler.Shared.Contracts.CustomEvents;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.ServiceProfiler.Contract.Agent;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services;

internal class AgentStatusService : IAgentStatusService
{
    private readonly IAgentStatusSender _agentStatusSender;
    private readonly IRoleNameSource _roleNameSource;
    private readonly IRoleInstanceSource _roleInstanceSource;
    private ProfilerAgentStatus? _current = null;

    public AgentStatusService(
        IAgentStatusSender agentStatusSender,
        IRoleNameSource roleNameSource, IRoleInstanceSource roleInstanceSource)
    {
        _agentStatusSender = agentStatusSender ?? throw new System.ArgumentNullException(nameof(agentStatusSender));
        _roleNameSource = roleNameSource ?? throw new System.ArgumentNullException(nameof(roleNameSource));
        _roleInstanceSource = roleInstanceSource ?? throw new System.ArgumentNullException(nameof(roleInstanceSource));
    }

    public ProfilerAgentStatus Current => _current ?? throw new System.InvalidOperationException("Agent status has not been initialized.");

    public async ValueTask<ProfilerAgentStatus> InitializeAsync(CancellationToken cancellationToken)
    {
        _current = await CreateNewStatusAsync(
            _roleNameSource.CloudRoleName,
            _roleInstanceSource.CloudRoleInstance,
            cancellationToken).ConfigureAwait(false);

        await _agentStatusSender.SendAsync(_current, cancellationToken).ConfigureAwait(false);

        return _current;
    }

    public ValueTask<ProfilerAgentStatus> UpdateAsync(ProfilerAgentStatus agentStatus, CancellationToken cancellationToken)
    {
        throw new System.NotImplementedException();
    }

    private ValueTask<ProfilerAgentStatus> CreateNewStatusAsync(string roleName, string roleInstance, CancellationToken cancellationToken)
    {
        return new ValueTask<ProfilerAgentStatus>(new ProfilerAgentStatus
        {
            Timestamp = System.DateTime.UtcNow,
            Status = AgentStatus.Active,
            RoleName = roleName,
            RoleInstance = roleInstance
        });
    }
}