using Microsoft.ApplicationInsights.Profiler.Shared.Contracts;
using Microsoft.ApplicationInsights.Profiler.Shared.Contracts.CustomEvents;
using Microsoft.ApplicationInsights.Profiler.Shared.Orchestrations;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.RoleNames;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ServiceProfiler.Contract.Agent.Profiler;
using Microsoft.ServiceProfiler.Orchestration;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services;

internal sealed class AgentStatusService : IAgentStatusService, IDisposable
{
    // Semaphore to protect the initialization and status update so that the initialization would only happen once.
    private readonly SemaphoreSlim _semaphoreSlim = new(initialCount: 1, maxCount: 1);

    // The current status and the reason for the status. Null if not initialized.
    private (string Reason, AgentStatus Status)? _current = null;

    private readonly IAgentStatusSender _agentStatusSender;
    private readonly IProfilerSettingsService _profilerSettingsService;
    private readonly IRoleNameSource _roleNameSource;
    private readonly IRoleInstanceSource _roleInstanceSource;
    private readonly UserConfigurationBase _userConfiguration;
    private readonly ILogger _logger;

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

    /// <summary>
    /// Event fired when the agent status changes.
    /// </summary>
    public event Func<AgentStatus, string, Task>? StatusChanged;

    public AgentStatus Current => _current?.Status ?? throw new InvalidOperationException("Agent status has not been initialized.");

    public async ValueTask<AgentStatus> InitializeAsync(CancellationToken cancellationToken)
    {

        _semaphoreSlim.Wait(cancellationToken);
        try
        {
            // The initialization has already happened before.
            if (_current is not null)
            {
                return _current.Value.Status;
            }

            // Waiting for the initialization of the settings service for 5 seconds.
            // This is the best sequence to ensure the settings are ready before we read the status.
            // However, if the settings service is not ready in 5 seconds, we will proceed anyway.
            _logger.LogTrace("Waiting for profiler settings service to be ready.");
            if (!await _profilerSettingsService.WaitForInitializedAsync(RemoteSettingsServiceBase.DefaultInitializationTimeout, cancellationToken).ConfigureAwait(false))
            {
                _logger.LogDebug("Profiler settings service is not ready in given time: {Timeout}. Proceeding anyway.", RemoteSettingsServiceBase.DefaultInitializationTimeout);
            }
            else
            {
                _logger.LogTrace("Profiler settings service ready.");
            }

            // Listen to settings changes.
            _profilerSettingsService.SettingsUpdated += OnProfilerSettingsUpdated;

            AgentStatus initialStatus = GetChangedStatusOrNull() ?? throw new InvalidOperationException("Failed to create initial agent status.");

            ReportStatusChange(initialStatus, "Initialization");

            return initialStatus;
        }
        finally
        {
            _semaphoreSlim.Release();
        }
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
            (string reason, AgentStatus status) = _current.Value;

            // Fire & forget
            await UpdateAsync(status, reason, CancellationToken.None).ConfigureAwait(false);
            await RaiseStatusChangedAsync(status, reason).ConfigureAwait(false);

            // Unless interrupted, the reason for the next update will be "Refresh".
            _current = ("Refresh", _current.Value.Status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update agent status regularly.");
        }
    }

    private async Task RaiseStatusChangedAsync(AgentStatus status, string reason)
    {
        Delegate[]? handlers = StatusChanged?.GetInvocationList();
        if (handlers is null || handlers.Length == 0)
        {
            return;
        }

        IEnumerable<Task> tasks = handlers
            .Cast<Func<AgentStatus, string, Task>>()
            .Select(handler => handler.Invoke(status, reason));

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private void OnProfilerSettingsUpdated(SettingsContract contract)
    {
        _logger.LogInformation("Profiler settings updated. Checking for agent status changes.");

        // Check if the settings request a different agent status. If it is, change the status.
        AgentStatus? newProfilerAgentStatus = GetChangedStatusOrNull();
        if (newProfilerAgentStatus is null)
        {
            _logger.LogDebug("No changes in agent status detected.");
            return;
        }
        _logger.LogInformation("Detected new agent status: {Status}.", newProfilerAgentStatus);

        ReportStatusChange(newProfilerAgentStatus.Value, "ProfilerSettingsChanged");
    }

    private void ReportStatusChange(AgentStatus newStatus, string reason)
    {
        // Update the status.
        _current = (reason, newStatus);

        // Very first time, create the timer.
        if (_statusUpdateTimer is null)
        {
            _statusUpdateTimer = new Timer(StatusTimerCallback, null, TimeSpan.Zero, DefaultStatusUpdateInterval);
            return;
        }

        // If the timer already exists, change trigger the timer immediately.
        _statusUpdateTimer?.Change(TimeSpan.Zero, DefaultStatusUpdateInterval);
    }

    /// <summary>
    /// Sends the agent status to the backend.
    /// </summary>
    private async ValueTask<ProfilerAgentStatus> UpdateAsync(AgentStatus agentStatus, string reason, CancellationToken cancellationToken)
    {
        ProfilerAgentStatus agentStatusRecord = new()
        {
            Timestamp = DateTime.UtcNow,
            Status = agentStatus,
            RoleName = _roleNameSource.CloudRoleName,
            RoleInstance = _roleInstanceSource.CloudRoleInstance
        };

        await _agentStatusSender.SendAsync(agentStatusRecord, reason, cancellationToken).ConfigureAwait(false);
        return agentStatusRecord;
    }

    /// <summary>
    /// Creates a new agent status.
    /// If the status has not changed, returns null.
    /// </summary>
    /// <returns>The new status or null if the status didn't change.</returns>
    private AgentStatus? GetChangedStatusOrNull()
    {
        AgentStatus newStatus = GetAgentStatus();

        if (_current is not null && _current.Value.Status == newStatus)
        {
            _logger.LogDebug("Agent status has not changed. Current status: {Status}", newStatus);
            return null;
        }

        return newStatus;
    }

    /// <summary>
    /// Get the agent status. Here's the logic:
    /// 1. If the settings are available, use the settings from ProfilerSettingsService.CurrentSettings.
    /// 2. If the settings are not available, get the local settings from UserConfigurationBase.
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
            if (TryGetMatchedItem(currentRoleName, currentRoleInstance, agentStatusGraph, out AgentStatusItem? match))
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

        // 2. If settings are not available, get the local settings from UserConfigurationBase.
        _logger.LogWarning("No agent status graph found in settings. Using local status.");
        return _userConfiguration.ActivatedOnStart ? AgentStatus.Active : AgentStatus.Inactive;
    }

    private bool TryGetMatchedItem(string currentRoleName, string currentRoleInstance, AgentStatusGraph agentStatusGraph, [NotNullWhen(true)] out AgentStatusItem? match)
    {
        string debug = JsonSerializer.Serialize(agentStatusGraph, new JsonSerializerOptions { WriteIndented = true });
        _logger.LogWarning("Agent status graph: {Graph}", debug);

        // Special case for "Unknown" role name.
        if (string.Equals(currentRoleName, UnknownRoleNameDetector.RoleName, StringComparison.Ordinal))
        {
            string fallbackRoleName = GetFallbackRoleName();
            _logger.LogWarning("Current role name is 'Unknown'. Fallback to default role name '{DefaultRoleName}'.", fallbackRoleName);

            match = agentStatusGraph.Statuses.FirstOrDefault(item =>
                string.Equals(item.RoleName, fallbackRoleName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.RoleInstance, currentRoleInstance, StringComparison.OrdinalIgnoreCase));

            if (match is not null)
            {
                _logger.LogWarning("Found matching agent status: {Status} for fallback role name '{RoleName}' and instance '{RoleInstance}'.", match.Status, fallbackRoleName, currentRoleInstance);
                return true;
            }
        }

        // Find the matching status for the current role name and instance.
        match = agentStatusGraph.Statuses.FirstOrDefault(item =>
            string.Equals(item.RoleName, currentRoleName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.RoleInstance, currentRoleInstance, StringComparison.OrdinalIgnoreCase));

        return match is not null;
    }

    private static string GetFallbackRoleName()
    {
        using Process process = Process.GetCurrentProcess();
        string processName = process.ProcessName;
        string roleName = $"unknown_service:{processName}";
        return roleName;
    }

    public void Dispose()
    {
        _semaphoreSlim.Dispose();
    }
}