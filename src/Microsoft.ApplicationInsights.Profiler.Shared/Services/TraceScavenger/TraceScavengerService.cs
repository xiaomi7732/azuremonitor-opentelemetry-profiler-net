//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights.Profiler.Shared.Contracts;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ServiceProfiler.Contract.Agent.Profiler;
using Microsoft.ServiceProfiler.Utilities;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services.TraceScavenger;

internal class TraceScavengerService : BackgroundService
{
    private readonly ILogger _logger;
    private readonly TraceScavengerServiceOptions _options;
    private readonly FileScavenger _fileScavenger;
    private readonly IAgentStatusService _agentStatusService;
    private readonly UserConfigurationBase _userConfiguration;
    private CancellationTokenSource? _cancellationTokenSource = null;
    private Task? _scavengerTask; // background loop task

    public TraceScavengerService(
        IOptions<UserConfigurationBase> userConfiguration,
        FileScavenger fileScavenger,
        IAgentStatusService agentStatusService,
        ILogger<TraceScavengerService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _userConfiguration = userConfiguration?.Value ?? throw new ArgumentNullException(nameof(userConfiguration));
        _options = _userConfiguration.TraceScavenger ?? throw new ArgumentNullException(nameof(userConfiguration));
        _fileScavenger = fileScavenger ?? throw new ArgumentNullException(nameof(fileScavenger));
        _agentStatusService = agentStatusService ?? throw new ArgumentNullException(nameof(agentStatusService));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_userConfiguration.IsDisabled)
        {
            _logger.LogDebug("No trace scavenger when the profiler is disabled.");
            return;
        }

        TimeSpan initialDelay = _options.InitialDelay;
        _logger.LogInformation("{serviceName} started. Initial delay: {delay}, Grace period from last access: {gracePeriod}", nameof(TraceScavengerService), initialDelay, _options.GracePeriod);
        await Task.Delay(initialDelay, stoppingToken).ConfigureAwait(false);

        AgentStatus initialStatus = await _agentStatusService.InitializeAsync(stoppingToken).ConfigureAwait(false);
        await OnAgentStatusChanged(initialStatus, "Initial status").ConfigureAwait(false);
        _agentStatusService.StatusChanged += OnAgentStatusChanged;
    }

    private Task OnAgentStatusChanged(AgentStatus status, string _)
    {
        switch (status)
        {
            case AgentStatus.Active:
                if (_scavengerTask is not null && !_scavengerTask.IsCompleted)
                {
                    _logger.LogDebug("Trace scavenger already running.");
                    return Task.CompletedTask; // already active
                }

                CancellationTokenSource newCancellationTokenSource = new();
                CancellationTokenSource? oldCancellationTokenSource = Interlocked.Exchange(ref _cancellationTokenSource, newCancellationTokenSource);
                oldCancellationTokenSource?.Cancel();
                oldCancellationTokenSource?.Dispose();
                _scavengerTask = Task.Run(() => RunScavengerLoopAsync(newCancellationTokenSource.Token));
                break;
            case AgentStatus.Inactive:
                _logger.LogDebug("Agent status is {status}, stopping trace scavenger.", status);
                _cancellationTokenSource?.Cancel();
                break;
            default:
                _logger.LogWarning("Unknown agent status: {status}. No action taken.", status);
                break;
        }
        return Task.CompletedTask;
    }

    private async Task RunScavengerLoopAsync(CancellationToken token)
    {
        _logger.LogDebug("Trace scavenger loop started.");
        while (!token.IsCancellationRequested)
        {
            try
            {
                _fileScavenger.Run(token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred when running trace scavenger: {message}", ex.Message);
            }
            try
            {
                await Task.Delay(_options.Interval > TimeSpan.Zero ? _options.Interval : TimeSpan.FromSeconds(5), token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        }
        _logger.LogDebug("Trace scavenger loop stopped.");
    }

    public override void Dispose()
    {
        _agentStatusService.StatusChanged -= OnAgentStatusChanged;

        try
        {
            _cancellationTokenSource?.Cancel();
            _scavengerTask?.Wait(TimeSpan.FromSeconds(5));
        }
        catch { /* ignore */ }
        finally
        {
            _cancellationTokenSource?.Dispose();
        }

        base.Dispose();
    }
}
