//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using Microsoft.ApplicationInsights.Profiler.Shared.Contracts;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ServiceProfiler.Contract.Agent.Profiler;
using Microsoft.ServiceProfiler.Orchestration;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Orchestrations;

///<summary>
/// Orchestrator for the EventPipe profiler. Starts all provided scheduling policies concurrently.
///</summary>
internal abstract class OrchestratorEventPipe : Orchestrator
{
    private readonly IReadOnlyCollection<SchedulingPolicy> _policyCollection;
    private readonly IServiceProfilerProvider _profilerProvider;
    private readonly IAgentStatusService _agentStatusService;
    private readonly IResourceUsageSource _resourceUsageSource;
    private ILogger _logger;
    private TimeSpan _initialDelay;

    // Allows at most 1 policy to change the value.
    private readonly SemaphoreSlim _policyChangeHandler = new(1, 1);

    // Refer to: https://stackoverflow.com/questions/67732623/is-it-safe-to-dispose-a-semaphoreslim-while-waiting-for-pending-operations-to-ca
    private readonly ConcurrentDictionary<Task, byte> _semaphoreTasks = new();

    private SchedulingPolicy? _currentProfilingPolicy = null;

    private CancellationTokenSource _cancellationTokenSource = new();

    private SemaphoreSlim _statusChangeSemaphore = new(1, 1);

    // Managed via Interlocked/Volatile; explicit 'volatile' not needed and causes CS0420 with ref operations.
    private Task _runningSchedules = Task.CompletedTask;

    public OrchestratorEventPipe(
        IServiceProfilerProvider profilerProvider,
        IOptions<UserConfigurationBase> config,
        IEnumerable<SchedulingPolicy> policyCollection,
        IDelaySource delaySource,
        IAgentStatusService agentStatusService,
        IResourceUsageSource resourceUsageSource,
        ILogger<OrchestratorEventPipe> logger) : base(delaySource)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _policyCollection = (policyCollection ?? throw new ArgumentNullException(nameof(policyCollection)))
            .ToList().AsReadOnly();
        _profilerProvider = profilerProvider ?? throw new ArgumentNullException(nameof(profilerProvider));
        _agentStatusService = agentStatusService ?? throw new ArgumentNullException(nameof(agentStatusService));
        _resourceUsageSource = resourceUsageSource ?? throw new ArgumentNullException(nameof(resourceUsageSource));
        _initialDelay = config.Value.InitialDelay;
    }

    /// <summary>
    /// Starts the orchestrator
    /// </summary>
    public async override Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Starting the orchestrator.");
        await Task.WhenAll(CreateInitializationTasks(cancellationToken)).ConfigureAwait(false);

        int activatedCount = ActivateSchedulePolicies();

        if (activatedCount > 0)
        {
            _logger.LogDebug("{count} scheduling policies has been activated.", activatedCount);
            if (_initialDelay != TimeSpan.Zero)
            {
                _logger.LogInformation("Initial delay for Application Insights Profiler.");
                await DelaySource.Delay(_initialDelay, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Finish initial delay. Profiling is activated.");
            }

            _agentStatusService.StatusChanged += OnAgentStatusChanged;
        }
        else
        {
            _logger.LogError("No scheduling policy has been activated.");
        }
    }

    private IEnumerable<Task> CreateInitializationTasks(CancellationToken cancellationToken)
    {
        if (_resourceUsageSource is ResourceUsageSource resourceUsageSource)
        {
            yield return resourceUsageSource.StartAsync(cancellationToken);
        }
        yield return _agentStatusService.InitializeAsync(cancellationToken).AsTask();
    }

    private async Task OnAgentStatusChanged(AgentStatus status, string reason)
    {
        _logger.LogDebug("Agent status changed to {status} for the reason of {reason}.", status, reason);

        await _statusChangeSemaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            switch (status)
            {
                case AgentStatus.Active:
                    CancellationTokenSource newCancellationTokenSource = new();
                    CancellationTokenSource oldCancellationTokenSource = Interlocked.Exchange(ref _cancellationTokenSource, newCancellationTokenSource);
                    oldCancellationTokenSource.Cancel();
                    oldCancellationTokenSource.Dispose();

                    // Go through each provided policy and start them
                    List<Task> policyAsync = [];
                    foreach (SchedulingPolicy policy in _policyCollection)
                    {
                        policyAsync.Add(policy.StartPolicyAsync(newCancellationTokenSource.Token));
                    }

                    _logger.LogDebug("Starting all scheduling policies.");
                    // Atomically start schedules exactly once when none are currently running.
                    while (true)
                    {
                        var current = Volatile.Read(ref _runningSchedules);
                        if (current != null && !current.IsCompleted)
                        {
                            _logger.LogWarning("The schedules are already running.");
                            break;
                        }

                        var schedulesTask = Task.WhenAll(policyAsync);
                        if (Interlocked.CompareExchange(ref _runningSchedules, schedulesTask, current) == current)
                        {
                            _ = schedulesTask.ContinueWith(t =>
                            {
                                if (t.IsFaulted)
                                {
                                    _logger.LogError(t.Exception?.Flatten(), "Scheduling policies task faulted.");
                                }
                                else if (t.IsCanceled)
                                {
                                    _logger.LogDebug("Scheduling policies task canceled.");
                                }
                                else
                                {
                                    _logger.LogDebug("Scheduling policies task completed successfully.");
                                }
                            }, TaskScheduler.Default);
                            _logger.LogDebug("Started all scheduling policies.");
                            break;
                        }
                        // Another thread won; retry to observe its state.
                    }
                    break;
                case AgentStatus.Inactive:
                    if (!_cancellationTokenSource.IsCancellationRequested)
                    {
                        _cancellationTokenSource.Cancel();
                        _cancellationTokenSource.Dispose();
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(status));
            }
        }
        catch (OperationCanceledException ex) when (ex.CancellationToken == _cancellationTokenSource.Token)
        {
            _logger.LogDebug("Operation cancelled to change the agent status to {status} for the reason of {reason}.", status, reason);
        }
        finally
        {
            _statusChangeSemaphore.Release();
        }
    }

    public override async Task<bool> StartProfilingAsync(SchedulingPolicy policy, CancellationToken cancellationToken)
    {
        _logger.LogTrace("Start profiling is requested by {source}", policy.Source);

        if (policy is null)
        {
            throw new ArgumentNullException(nameof(policy));
        }

        Task<bool> waitSemaphoreTask = _policyChangeHandler.WaitAsync(TimeSpan.FromMilliseconds(500), cancellationToken);
        try
        {
            _semaphoreTasks.TryAdd(waitSemaphoreTask, 0);
            if (await waitSemaphoreTask.ConfigureAwait(false))
            {
                try
                {
                    if (_currentProfilingPolicy == null)
                    {
                        bool result = await _profilerProvider.StartServiceProfilerAsync(policy, cancellationToken).ConfigureAwait(false);
                        if (result)
                        {
                            _logger.LogDebug("Profiling is started. Source: {source}", policy.Source);
                            _currentProfilingPolicy = policy;
                        }

                        return result;
                    }
                    else
                    {
                        _logger.LogTrace("Profiling is running by policy: {source}", _currentProfilingPolicy.Source);
                    }
                }
                finally
                {
                    _policyChangeHandler.Release();
                }
            }
            else
            {
                _logger.LogWarning("Can't get the handler to start a profiling. Requested by {newSource}. Current Profiling by: {currentSource}", policy.Source, _currentProfilingPolicy?.Source);
            }

            _logger.LogTrace("Profiling is running by policy: {source}", _currentProfilingPolicy?.Source);
            return false;
        }
        finally
        {
            _semaphoreTasks.TryRemove(waitSemaphoreTask, out _);
        }
    }

    public override async Task<bool> StopProfilingAsync(SchedulingPolicy policy, CancellationToken cancellationToken)
    {
        _logger.LogTrace("Stop profiling is requested by {sourceName}, threadId: {managedThreadId}", policy.Source, Thread.CurrentThread.ManagedThreadId);

        bool result;
        if (_currentProfilingPolicy == null)
        {
            _logger.LogTrace("Request stop profiling by {source} rejected. No profiling is running.", policy.Source);
            result = false;
        }
        else if (_currentProfilingPolicy != policy)
        {
            _logger.LogTrace("Stop profiling by {requestingSource} failed. The current profiling is requested by {currentSource}", policy.Source, _currentProfilingPolicy?.Source);
            result = false;
        }
        else
        {
            Task<bool> waitSemaphoreTask = _policyChangeHandler.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            try
            {
                _semaphoreTasks.TryAdd(waitSemaphoreTask, 0);
                if (await waitSemaphoreTask.ConfigureAwait(false))
                {
                    try
                    {
                        if (_currentProfilingPolicy == policy)
                        {
                            _logger.LogDebug("Stopping Profiler by {source}", policy.Source);
                            result = await _profilerProvider.StopServiceProfilerAsync(policy, cancellationToken).ConfigureAwait(false);
                            if (result)
                            {
                                _logger.LogDebug("Profiler stopped by {source}", policy.Source);
                                _currentProfilingPolicy = null;
                            }
                        }
                        else
                        {
                            _logger.LogTrace("Can't stop profiling requested by {newSource}.", _currentProfilingPolicy.Source);
                            result = false;
                        }
                    }
                    catch
                    {
                        // Stop profiling can fail for various reasons. Check the current status to decide 
                        // whether to give back the current profiling handler.
                        if (_currentProfilingPolicy == policy && !_profilerProvider.IsProfilerRunning)
                        {
                            _currentProfilingPolicy = null;
                        }

                        throw;
                    }
                    finally
                    {
                        _policyChangeHandler.Release();
                    }
                }
                else
                {
                    _logger.LogWarning("Can't get the handler to stop a profiling. Requested by {newSource}. Current Profiling by: {currentSource}", policy.Source, _currentProfilingPolicy?.Source);
                    result = false;
                }
            }
            finally
            {
                _semaphoreTasks.TryRemove(waitSemaphoreTask, out _);
            }
        }

        return result;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _cancellationTokenSource.Cancel();
            _cancellationTokenSource.Dispose();

            _agentStatusService.StatusChanged -= OnAgentStatusChanged;
            _statusChangeSemaphore.Dispose();

            Task.WhenAll(_semaphoreTasks.Keys);
            _policyChangeHandler.Dispose();
        }
    }

    private int ActivateSchedulePolicies()
    {
        // TODO: Read the configuration to decide whether the policy should be activated or not.
        int activated = 0;
        foreach (SchedulingPolicy policy in _policyCollection)
        {
            policy.RegisterToOrchestrator(this);
            activated++;
        }

        return activated;
    }
}
