using Microsoft.ApplicationInsights.Profiler.Shared.Contracts;
using Microsoft.ApplicationInsights.Profiler.Shared.Contracts.CustomEvents;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ServiceProfiler.Contract.Agent.Profiler;
using Microsoft.ServiceProfiler.Orchestration;
using System;
using System.Linq;
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
        IOptions<UserConfigurationBase> userConfiguration,
        ILogger<AgentStatusService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _agentStatusSender = agentStatusSender ?? throw new ArgumentNullException(nameof(agentStatusSender));
        _profilerSettingsService = profilerSettingsService ?? throw new ArgumentNullException(nameof(profilerSettingsService));
        _roleNameSource = roleNameSource ?? throw new ArgumentNullException(nameof(roleNameSource));
        _roleInstanceSource = roleInstanceSource ?? throw new ArgumentNullException(nameof(roleInstanceSource));
        _userConfiguration = userConfiguration?.Value ?? throw new ArgumentNullException(nameof(userConfiguration));
    }

    public ProfilerAgentStatus Current => _current ?? throw new InvalidOperationException("Agent status has not been initialized.");

    public async ValueTask<ProfilerAgentStatus> InitializeAsync(CancellationToken cancellationToken)
    {
        _profilerSettingsService.SettingsUpdated += OnProfilerSettingsUpdated;

        _current = await CreateNewStatusAsync(
            _roleNameSource.CloudRoleName,
            _roleInstanceSource.CloudRoleInstance,
            cancellationToken).ConfigureAwait(false);

        await UpdateAsync(_current, "Initialization", cancellationToken).ConfigureAwait(false);

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
            await UpdateAsync(_current, "Refresh", CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update agent status regularly.");
        }
    }

    private async void OnProfilerSettingsUpdated(SettingsContract contract)
    {
        // When the status changed
        try
        {
            _logger.LogInformation("Profiler settings updated. Checking for agent status changes.");
            // Check if the settings request a different agent status. If it is, change the status.
            _current = await CreateNewStatusAsync(_roleNameSource.CloudRoleName, _roleInstanceSource.CloudRoleInstance, CancellationToken.None).ConfigureAwait(false);
            // Fire & forget
            await UpdateAsync(
                _current ?? throw new InvalidOperationException("Agent status has not been initialized."),
                "Settings updated",
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update agent status on new settings.");
        }
    }

    private async ValueTask<ProfilerAgentStatus> UpdateAsync(ProfilerAgentStatus agentStatus, string reason, CancellationToken cancellationToken)
    {
        await _agentStatusSender.SendAsync(agentStatus, reason, cancellationToken).ConfigureAwait(false);

        // For whatever reason, push the next status report after the default interval.
        _statusUpdateTimer?.Change(DefaultStatusUpdateInterval, DefaultStatusUpdateInterval);
        return agentStatus;
    }

    private ValueTask<ProfilerAgentStatus> CreateNewStatusAsync(string roleName, string roleInstance, CancellationToken cancellationToken)
    {
        return new ValueTask<ProfilerAgentStatus>(new ProfilerAgentStatus
        {
            Timestamp = DateTime.UtcNow,
            Status = GetAgentStatus(),
            RoleName = roleName,
            RoleInstance = roleInstance
        });
    }

    /// <summary>
    /// Get the agent status. Here's the logic:
    /// 1. If the settings are available, use the settings from ProfilerSettingsService.CurrentSettings.
    /// 2. If the settings are not available, get the local settings from UserConfigurationBase.
    /// 3. If no settings are available, return a default status (e.g., Active).
    /// </summary>
    private AgentStatus GetAgentStatus()
    {
        _logger.LogWarning("Get default settings' logic is not implemented yet.");

        string currentRoleName = _roleNameSource.CloudRoleName;
        string currentRoleInstance = _roleInstanceSource.CloudRoleInstance;

        // 1. Get and use the settings from ProfilerSettingsService.CurrentSettings.
        AgentStatusGraph? agentStatusGraph = _profilerSettingsService.CurrentSettings?.AgentStatusGraph;
        if (agentStatusGraph is not null)
        {
            // Find the matching status for the current role name and instance.
            AgentStatusItem? match = agentStatusGraph.Statuses.FirstOrDefault(item =>
                string.Equals(item.RoleName, currentRoleName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.RoleInstance, currentRoleInstance, StringComparison.OrdinalIgnoreCase));

            if (match is not null)
            {
                _logger.LogWarning("Found matching agent status: {Status} for role name '{RoleName}' and instance '{RoleInstance}'.", match.Status, currentRoleName, currentRoleInstance);
                return match.Status;
            }

            _logger.LogWarning("No matching agent status found for role name '{RoleName}' and instance '{RoleInstance}'. Using default status.", currentRoleName, currentRoleInstance);
            return agentStatusGraph.DefaultStatus;
        }

        _logger.LogWarning("No agent status graph found in settings. Using local status.");
        // 2. If settings are not available, get the local settings from UserConfigurationBase.

        // TODO: Get the initial settings from ProfilerSettingsService.CurrentSettings.
        return AgentStatus.Inactive; // Default status can be set based on the initial settings or configuration.
    }
}