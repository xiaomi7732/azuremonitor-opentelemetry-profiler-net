using Microsoft.ApplicationInsights.Profiler.Shared.Contracts;
using Microsoft.ApplicationInsights.Profiler.Shared.Contracts.CustomEvents;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ServiceProfiler.Contract.Agent.Profiler;
using Microsoft.ServiceProfiler.Orchestration;
using System;
using System.Diagnostics.CodeAnalysis;
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

        _current = CreateNewStatus(
            _roleNameSource.CloudRoleName,
            _roleInstanceSource.CloudRoleInstance);

        await UpdateAsync(_current, "Initialization", cancellationToken).ConfigureAwait(false);

        // Setup the timer to periodically update the agent status.
        _statusUpdateTimer = new Timer(StatusTimerCallback, state: null, dueTime: DefaultStatusUpdateInterval, period: DefaultStatusUpdateInterval);

        return _current;
    }

    private async void StatusTimerCallback(object? state)
    {
        if (_current is null)
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
            _current = CreateNewStatus(_roleNameSource.CloudRoleName, _roleInstanceSource.CloudRoleInstance);

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

    private ProfilerAgentStatus CreateNewStatus(string roleName, string roleInstance)
        => new()
        {
            Timestamp = DateTime.UtcNow,
            Status = GetAgentStatus(),
            RoleName = roleName,
            RoleInstance = roleInstance
        };

    /// <summary>
    /// Get the agent status. Here's the logic:
    /// 1. If the settings are available, use the settings from ProfilerSettingsService.CurrentSettings.
    /// 2. If the settings are not available, get the local settings from UserConfigurationBase.
    /// 3. If no settings are available, return a default status (e.g., Active).
    /// </summary>
    private AgentStatus GetAgentStatus()
    {
        string currentRoleName = _roleNameSource.CloudRoleName;
        string currentRoleInstance = _roleInstanceSource.CloudRoleInstance;

        _logger.LogWarning("Looking for agent status for role name '{RoleName}' and instance '{RoleInstance}'.", currentRoleName, currentRoleInstance);

        // 1. Get and use the settings from ProfilerSettingsService.CurrentSettings.
        AgentStatusGraph? agentStatusGraph = _profilerSettingsService.CurrentSettings?.AgentStatusGraph;

        if (agentStatusGraph is not null)
        {
            if (TryGetMatchedItem(currentRoleName, currentRoleInstance, agentStatusGraph, out AgentStatusItem match))
            {
                _logger.LogWarning("Found matching agent status: {Status} for role name '{RoleName}' and instance '{RoleInstance}'.", match.Status, currentRoleName, currentRoleInstance);
                return match.Status;
            }

            _logger.LogWarning("No matching agent status found for role name '{RoleName}' and instance '{RoleInstance}'. Using default status.", currentRoleName, currentRoleInstance);
            return agentStatusGraph.DefaultStatus;
        }
        else
        {
            _logger.LogWarning("Agent status graph is null in settings");
        }

        _logger.LogWarning("No agent status graph found in settings. Using local status.");
        // 2. If settings are not available, get the local settings from UserConfigurationBase.
        // TODO: Get the initial settings from ProfilerSettingsService.CurrentSettings.
        return AgentStatus.Inactive; // Default status can be set based on the initial settings or configuration.
    }

    private bool TryGetMatchedItem(string currentRoleName, string currentRoleInstance, AgentStatusGraph agentStatusGraph, [NotNullWhen(true)] out AgentStatusItem? match)
    {
        // Special case for "Unknown" role name.
        if (string.Equals(currentRoleName, "Unknown", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Current role name is 'Unknown'. This should only happen when the agent is running on non-production environment.");

            match = agentStatusGraph.Statuses.FirstOrDefault(item =>
                item.RoleName.StartsWith("unknown_service:", StringComparison.Ordinal) &&
                string.Equals(item.RoleInstance, currentRoleInstance, StringComparison.OrdinalIgnoreCase));
        }

        // Find the matching status for the current role name and instance.
        match = agentStatusGraph.Statuses.FirstOrDefault(item =>
            string.Equals(item.RoleName, currentRoleName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.RoleInstance, currentRoleInstance, StringComparison.OrdinalIgnoreCase));

        return match is not null;
    }
}