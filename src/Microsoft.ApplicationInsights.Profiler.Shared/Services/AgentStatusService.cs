using Microsoft.ApplicationInsights.Profiler.Shared.Contracts;
using Microsoft.ApplicationInsights.Profiler.Shared.Contracts.CustomEvents;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.ServiceProfiler.Contract.Agent;
using Microsoft.ServiceProfiler.Contract.Agent.Profiler;
using Microsoft.ServiceProfiler.Orchestration;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services;

internal class AgentStatusService : IAgentStatusService
{
    private readonly IAgentStatusSender _agentStatusSender;
    private readonly IProfilerSettingsService _profilerSettingsService;
    private readonly IRoleNameSource _roleNameSource;
    private readonly IRoleInstanceSource _roleInstanceSource;
    private readonly UserConfigurationBase _userConfiguration;
    private readonly ILogger _logger;
    private ProfilerAgentStatus? _current = null;

#if DEBUG
    private static readonly TimeSpan DefaultStatusUpdateInterval = TimeSpan.FromMinutes(2);
#else
    private static readonly TimeSpan DefaultStatusUpdateInterval = TimeSpan.FromHours(2);
#endif

    private Timer? _statusUpdateTimer;

    public AgentStatusService(
        IAgentStatusSender agentStatusSender,
        IProfilerSettingsService profilerSettingsService,
        IRoleNameSource roleNameSource, IRoleInstanceSource roleInstanceSource,
        UserConfigurationBase userConfiguration,
        ILogger<AgentStatusService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _agentStatusSender = agentStatusSender ?? throw new ArgumentNullException(nameof(agentStatusSender));
        _profilerSettingsService = profilerSettingsService ?? throw new ArgumentNullException(nameof(profilerSettingsService));
        _roleNameSource = roleNameSource ?? throw new ArgumentNullException(nameof(roleNameSource));
        _roleInstanceSource = roleInstanceSource ?? throw new ArgumentNullException(nameof(roleInstanceSource));
        _userConfiguration = userConfiguration ?? throw new ArgumentNullException(nameof(userConfiguration));
    }

    public ProfilerAgentStatus Current => _current ?? throw new InvalidOperationException("Agent status has not been initialized.");

    public async ValueTask<ProfilerAgentStatus> InitializeAsync(CancellationToken cancellationToken)
    {
        _profilerSettingsService.SettingsUpdated += OnProfilerSettingsUpdated;

        _current = await CreateNewStatusAsync(
            _roleNameSource.CloudRoleName,
            _roleInstanceSource.CloudRoleInstance,
            cancellationToken).ConfigureAwait(false);

        await UpdateAsync(_current, cancellationToken).ConfigureAwait(false);

        // Setup the timer to periodically update the agent status.
        _statusUpdateTimer = new Timer(StatusTimerCallback, state: null, dueTime: DefaultStatusUpdateInterval, period: DefaultStatusUpdateInterval);

        return _current;
    }


    private async void StatusTimerCallback(object? state)
    {
        if (_current == null)
        {
            _logger.LogWarning("Agent status is not initialized. Cannot update status.");
            return;
        }

        try
        {
            // Fire & forget
            await UpdateAsync(_current, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        { 
            _logger.LogError(ex, "Failed to update agent status regularly.");
        }
    }

    private async void OnProfilerSettingsUpdated(SettingsContract contract)
    {
        // Check if the settings request a different agent status. If it is, change the status.
        _logger.LogWarning("TODO: update the current with the new settings contract.");

        // When the status changed
        try
        {
            // Fire & forget
            await UpdateAsync(_current ?? throw new InvalidOperationException("Agent status has not been initialized."), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        { 
            _logger.LogError(ex, "Failed to update agent status on new settings.");
        }
    }

    public async ValueTask<ProfilerAgentStatus> UpdateAsync(ProfilerAgentStatus agentStatus, CancellationToken cancellationToken)
    {
        await _agentStatusSender.SendAsync(agentStatus, cancellationToken).ConfigureAwait(false);

        // For whatever reason, push the next status report after the default interval.
        _statusUpdateTimer?.Change(DefaultStatusUpdateInterval, DefaultStatusUpdateInterval);
        return agentStatus;
    }

    private ValueTask<ProfilerAgentStatus> CreateNewStatusAsync(string roleName, string roleInstance, CancellationToken cancellationToken)
    {
        return new ValueTask<ProfilerAgentStatus>(new ProfilerAgentStatus
        {
            Timestamp = DateTime.UtcNow,
            Status = GetDefaultAgentStatus(), 
            RoleName = roleName,
            RoleInstance = roleInstance
        });
    }

    private AgentStatus GetDefaultAgentStatus()
    { 
        _logger.LogWarning("Get default settings' logic is not implemented yet.");
        AgentStatus agentStatus = AgentStatus.Active;
        
        // 1. Get and use the settings from ProfilerSettingsService.CurrentSettings.

        // 2. If settings are not available, get the local settings from UserConfigurationBase.

        // TODO: Get the initial settings from ProfilerSettingsService.CurrentSettings.
        return agentStatus; // Default status can be set based on the initial settings or configuration.
    }
}