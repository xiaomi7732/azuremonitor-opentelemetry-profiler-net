//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using Microsoft.ApplicationInsights.Profiler.Core.EventListeners;
using Microsoft.ApplicationInsights.Profiler.Core.Logging;
using Microsoft.ApplicationInsights.Profiler.Core.TraceControls;
using Microsoft.ApplicationInsights.Profiler.Shared.Contracts;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.ServiceProfiler.Agent.Exceptions;
using Microsoft.ServiceProfiler.Orchestration;
using Microsoft.ServiceProfiler.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.ApplicationInsights.Profiler.Core;

internal sealed class ServiceProfilerProvider : IServiceProfilerProvider, IDisposable
{

    private const string StartProfilerTriggered = "StartProfiler triggered.";
    private const string StartProfilerSucceeded = "StartProfiler succeeded.";
    private const string StopProfilerTriggered = "StopProfiler triggered.";
    private const string StopProfilerSucceeded = "StopProfiler succeeded.";
    private const string StopProfilerFailed = "StopProfiler failed.";

    private Guid _appId;
    private readonly ILogger _logger;
    private readonly IServiceProfilerContext _serviceProfilerContext;
    private readonly ITraceControl _traceControl;
    private readonly IEventPipeTelemetryTracker _telemetryTracker;
    private readonly IResourceUsageSource _resourceUsageSource;

    private float _sessionCPUUsage;
    private float _sessionMemoryUsage;

    private IEnumerator<ITraceSessionListener>? _sessionListeners;
    internal ITraceSessionListener? SessionListener { get; private set; }

    private readonly ITraceSessionListenerFactory _traceSessionListenerFactory;

    private readonly IAppInsightsSinks _appInsightsSinks;
    private readonly IUserCacheManager _userCacheManager;
    private readonly IPostStopProcessorFactory _postStopProcessorFactory;
    private readonly ITraceFileFormatDefinition _traceFileFormatDefinition;
    private readonly AppInsightsProfileFetcher _appInsightsProfileFetcher;
    private string? _currentTraceFilePath;

    private SemaphoreSlim _singleProfilingSemaphore = new SemaphoreSlim(1, 1);
    public bool IsProfilerRunning => _singleProfilingSemaphore.CurrentCount == 0;

    // Set once Dispose() has run (e.g. during host shutdown). Used to short-circuit the semaphore
    // release so a stop racing with disposal does not touch a disposed semaphore.
    private volatile bool _disposed;

    public ServiceProfilerProvider(
        IServiceProfilerContext serviceProfilerContext,
        ITraceControl traceControl,
        ITraceSessionListenerFactory traceSessionListenerFactory,
        IEventPipeTelemetryTracker telemetryTracker,
        IAppInsightsSinks appInsightsSinks,
        IUserCacheManager userCacheManager,
        IPostStopProcessorFactory postStopProcessorFactory,
        ITraceFileFormatDefinition traceFileFormatDefinition,
        AppInsightsProfileFetcher appInsightsProfileFetcher,
        IResourceUsageSource resourceUsageSource,
        ILogger<ServiceProfilerProvider> logger)
    {
        // Required
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _userCacheManager = userCacheManager ?? throw new ArgumentNullException(nameof(userCacheManager));
        _postStopProcessorFactory = postStopProcessorFactory ?? throw new ArgumentNullException(nameof(postStopProcessorFactory));
        _traceFileFormatDefinition = traceFileFormatDefinition ?? throw new ArgumentNullException(nameof(traceFileFormatDefinition));
        _appInsightsProfileFetcher = appInsightsProfileFetcher ?? throw new ArgumentNullException(nameof(appInsightsProfileFetcher));
        _serviceProfilerContext = serviceProfilerContext ?? throw new ArgumentNullException(nameof(serviceProfilerContext));


        _traceControl = traceControl ?? throw new ArgumentNullException(nameof(traceControl));
        _traceSessionListenerFactory = traceSessionListenerFactory ?? throw new ArgumentNullException(nameof(traceSessionListenerFactory));
        _telemetryTracker = telemetryTracker ?? throw new ArgumentNullException(nameof(telemetryTracker));
        _appInsightsSinks = appInsightsSinks ?? throw new ArgumentNullException(nameof(appInsightsSinks));
        _resourceUsageSource = resourceUsageSource ?? throw new ArgumentNullException(nameof(resourceUsageSource));
    }

    /// <summary>
    /// Starts the Service Profiler using the configured scheduler.
    /// </summary>
    public async Task<bool> StartServiceProfilerAsync(IProfilerSource source, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Entering {StartServiceProfilerAsync}.", nameof(StartServiceProfilerAsync));

        try
        {
            if (!await _singleProfilingSemaphore.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false))
            {
                _logger.LogDebug("No semaphore fetched within given time to check profiling state. There is another profiling in progress. Give it up.");
                return false;
            }
        }
        catch (Exception ex) when (ex is ObjectDisposedException || ex is OperationCanceledException)
        {
            _logger.LogDebug("Semaphore disposed or cancelled. Profiler won't start.");
            return false;
        }

        _logger.LogDebug("Got the semaphore. Try starting the Profiler.");
        bool profilerStarted = false;
        try
        {
            // Got the semaphore within given time. Try to start Profiler.
            _appInsightsSinks.LogInformation(StartProfilerTriggered);

            string localCacheFolder = _userCacheManager.TempTraceDirectory.FullName;
            Directory.CreateDirectory(localCacheFolder);

            _currentTraceFilePath = Path.ChangeExtension(Path.Combine(localCacheFolder, Guid.NewGuid().ToString()), _traceFileFormatDefinition.FileExtension);
            _logger.LogDebug("Trace File Path: {traceFilePath}", _currentTraceFilePath);

            try
            {
                // Capture resource usage at the beginning of the profiling session, before trace collection starts.
                _sessionCPUUsage = _resourceUsageSource.GetAverageCPUUsage();
                _sessionMemoryUsage = _resourceUsageSource.GetAverageMemoryUsage();
                _logger.LogDebug("Captured resource usage at session start: CPU={cpuUsage}, Memory={memoryUsage}", _sessionCPUUsage, _sessionMemoryUsage);

                _logger.LogDebug("Call TraceControl.Enable().");
                await _traceControl.EnableAsync(_currentTraceFilePath, cancellationToken).ConfigureAwait(false);
                profilerStarted = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start eventpipe profiling.");
                throw;
            }

            _appInsightsSinks.LogInformation(StartProfilerSucceeded);

            Guid authenticatedUserId = await GetAppIdOrEmptyAsync(cancellationToken).ConfigureAwait(false);
            _telemetryTracker.SetCustomerAppInfo(authenticatedUserId);

            _logger.LogDebug("Start to create trace session listener by its factory");
            _sessionListeners = _traceSessionListenerFactory.CreateTraceSessionListeners().GetEnumerator();
            ActivateNext(this, null);

            _logger.LogDebug("TraceSessionListener created.");
            _logger.LogInformation(TelemetryConstants.SessionStarted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Starting profiler failed.");
            throw;
        }
        finally
        {
            // Return the resource if profiler is not started.
            if (!profilerStarted)
            {
                _singleProfilingSemaphore.Release();
            }
        }

        return profilerStarted;
    }

    /// <summary>
    /// Stops the current Service Profiler.
    /// </summary>
    public async Task<bool> StopServiceProfilerAsync(IProfilerSource source, CancellationToken cancellationToken)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        bool profilerStopped = false;

        _appInsightsSinks.LogInformation(StopProfilerTriggered);
        _logger.LogDebug("Entering {stopServiceProfilerAsync}.", nameof(StopServiceProfilerAsync));

        // Sanity check
        if (!IsProfilerRunning)
        {
            _logger.LogDebug("Try to stop profiler while none is is running.");
            return false;
        }

        DateTime? currentSessionId = _traceControl.SessionStartUTC;
        if (currentSessionId is null)
        {
            throw new InvalidOperationException("Failed fetching session start time.");
        }

        try
        {
            // Copy resource usage values captured at session start before releasing the semaphore,
            // to avoid a race where a new session could overwrite these fields.
            float cpuUsage = _sessionCPUUsage;
            float memoryUsage = _sessionMemoryUsage;

            // Notice: Stop trace session listener has to be happen before calling ITraceControl.Disable().
            // Trace control disabling will take a while. We shall not gathering anything events when disable is happening.
            _logger.LogDebug("Disabling {sessionListener}", nameof(SessionListener));
            List<SampleActivity>? sampleActivities = SessionListener?.SampleActivities?.GetActivities()?.ToList();

            SessionListener?.Dispose();
            SessionListener = null;

            try
            {
                try
                {
                    await _traceControl.DisableAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Fail to disable EventPipe profiling.");
                    _appInsightsSinks.LogInformation(StopProfilerFailed);
                    throw;
                }

                // The profiler has stopped successfully once the trace is disabled. This is
                // independent of whether the trace is subsequently uploaded: uploading can be
                // skipped by design (e.g. no samples collected / upload disabled), which must not
                // be treated as a stop failure. Any unexpected upload failure is logged by the
                // post-stop processor so it can be investigated.
                profilerStopped = true;
                _appInsightsSinks.LogInformation(StopProfilerSucceeded);

                string currentTraceFilePath = _currentTraceFilePath ?? throw new InvalidOperationException("Current trace file path is not set. This should not happen. Please contact the project owner.");

                try
                {
                    await _postStopProcessorFactory.Create().PostStopProcessAsync(new PostStopOptions(
                        currentTraceFilePath,
                        currentSessionId.Value,
                        stampFrontendHostUrl: _serviceProfilerContext.StampFrontendEndpointUrl,
                        sampleActivities ?? Enumerable.Empty<SampleActivity>(),
                        source,
                        averageCPUUsage: cpuUsage,
                        averageMemoryUsage: memoryUsage
                        ), cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // The profiler already stopped successfully (the trace was disabled above);
                    // only the post-stop processing / trace upload failed. Surface the unexpected
                    // failure so it can be investigated, but do not fault the stop itself.
                    // Cancellation (OperationCanceledException) is excluded: it is a benign,
                    // caller-requested outcome (e.g. host shutdown) and is allowed to propagate
                    // rather than being logged as an error.
                    _logger.LogError(ex, "Post-stop processing (trace upload) failed after the profiler was stopped.");
                }
            }
            finally
            {
                // The inner try guarantees this runs on every path that started post-stop
                // processing, releasing the profiling semaphore exactly once. No additional
                // release is performed elsewhere, so a concurrent StartServiceProfilerAsync
                // cannot have its freshly acquired semaphore released by this stop.
                ReleaseSemaphoreForProfiling();
            }

            _logger.LogInformation(TelemetryConstants.SessionEnded);

            return profilerStopped;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Unexpected error happens on stopping service profiler tracing.");
            throw;
        }
    }

    private void ReleaseSemaphoreForProfiling()
    {
        // The provider is a singleton IDisposable. During host shutdown its Dispose() can run
        // concurrently with an in-flight (best-effort) stop, disposing the semaphore before this
        // release. Treat a disposed semaphore as a graceful no-op instead of surfacing a noisy
        // "Unexpected error happens on stopping service profiler tracing." ObjectDisposedException.
        if (_disposed)
        {
            return;
        }

        try
        {
            if (IsProfilerRunning)
            {
                _singleProfilingSemaphore.Release();
            }
        }
        catch (ObjectDisposedException)
        {
            _logger.LogDebug("Profiling semaphore already disposed (likely during host shutdown); skipping release.");
        }
    }

    private void ActivateNext(object sender, EventArgs? e)
    {
        if (SessionListener != null)
        {
            SessionListener.Poisoned -= ActivateNext;
            SessionListener.Dispose();
        }

        if (_sessionListeners is null)
        {
            _logger.LogInformation("There's no registered backup session listener.");
            return;
        }

        if (_sessionListeners.MoveNext())
        {
            SessionListener = _sessionListeners.Current;
            SessionListener.Poisoned += ActivateNext;
            _logger.LogDebug("Activating trace session listener: {traceSessionListener}", SessionListener.GetType().Name);
            SessionListener.Activate();
        }
        else
        {
            _logger.LogInformation("All session listeners failed to have valid samples. Profiling Session failed.");
        }
    }

    private async Task<Guid> GetAppIdOrEmptyAsync(CancellationToken cancellationToken)
    {
        if (_appId != Guid.Empty)
        {
            return _appId;
        }

        _logger.LogDebug("Fetching AppId from Application Insights using iKey: {iKey}", _serviceProfilerContext.AppInsightsInstrumentationKey);
        try
        {
            AppInsightsProfile result = await _appInsightsProfileFetcher.FetchProfileAsync(_serviceProfilerContext.AppInsightsInstrumentationKey, retryCount: 5);
            return _appId = result.AppId;
        }
        catch (InstrumentationKeyInvalidException ikie)
        {
            _logger.LogError(ikie, "Profiler Instrumentation Key is invalid.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error fetching app id.");
        }
        return Guid.Empty;
    }

    public void Dispose()
    {
        _disposed = true;
        _singleProfilingSemaphore?.Dispose();

        _sessionListeners?.Dispose();
        _sessionListeners = null;
    }
}
