//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using Microsoft.ApplicationInsights.Profiler.Core.Contracts;
using Microsoft.ApplicationInsights.Profiler.Core.EventListeners;
using Microsoft.ApplicationInsights.Profiler.Core.Logging;
using Microsoft.ApplicationInsights.Profiler.Core.SampleTransfers;
using Microsoft.ApplicationInsights.Profiler.Core.TraceControls;
using Microsoft.ApplicationInsights.Profiler.Shared.Contracts;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions.IPC;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ServiceProfiler.Orchestration;
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

    private readonly ILogger _logger;
    private readonly IServiceProfilerContext _serviceProfilerContext;
    private readonly ITraceControl _traceControl;
    private readonly IEventPipeTelemetryTracker _telemetryTracker;

    private IEnumerator<ITraceSessionListener> _sessionListeners;
    internal ITraceSessionListener SessionListener { get; private set; }

    private readonly ITraceSessionListenerFactory _traceSessionListenerFactory;

    private readonly IAppInsightsSinks _appInsightsSinks;
    private readonly IUserCacheManager _userCacheManager;
    private readonly IPostStopProcessorFactory _postStopProcessorFactory;
    private readonly ITraceFileFormatDefinition _traceFileFormatDefinition;
    private string _currentTraceFilePath;

    private SemaphoreSlim _singleProfilingSemaphore = new SemaphoreSlim(1, 1);
    public bool IsProfilerRunning => _singleProfilingSemaphore.CurrentCount == 0;

    public ServiceProfilerProvider(
        IServiceProfilerContext serviceProfilerContext,
        ITraceControl traceControl,
        ITraceSessionListenerFactory traceSessionListenerFactory,
        IEventPipeTelemetryTracker telemetryTracker,
        ICustomEventsTracker customEventsTracker,
        ITraceUploader traceUploader,
        IMetadataWriter metadataWriter,
        IOptions<UserConfiguration> serviceProfilerConfiguration,
        IAppInsightsSinks appInsightsSinks,
        INamedPipeClientFactory namedPipeClientFactory,
        IUploaderPathProvider uploaderPathProvider,
        ISerializationProvider serializer,
        IUserCacheManager userCacheManager,
        IPostStopProcessorFactory postStopProcessorFactory,
        ITraceFileFormatDefinition traceFileFormatDefinition,
        ILogger<ServiceProfilerProvider> logger)
    {
        // Required
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _userCacheManager = userCacheManager ?? throw new ArgumentNullException(nameof(userCacheManager));
        _postStopProcessorFactory = postStopProcessorFactory ?? throw new ArgumentNullException(nameof(postStopProcessorFactory));
        _traceFileFormatDefinition = traceFileFormatDefinition ?? throw new ArgumentNullException(nameof(traceFileFormatDefinition));
        _serviceProfilerContext = serviceProfilerContext ?? throw new ArgumentNullException(nameof(serviceProfilerContext));


        _traceControl = traceControl ?? throw new ArgumentNullException(nameof(traceControl));
        _traceSessionListenerFactory = traceSessionListenerFactory ?? throw new ArgumentNullException(nameof(traceSessionListenerFactory));
        _telemetryTracker = telemetryTracker ?? throw new ArgumentNullException(nameof(telemetryTracker));
        _appInsightsSinks = appInsightsSinks ?? throw new ArgumentNullException(nameof(appInsightsSinks));
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

            _currentTraceFilePath = Path.ChangeExtension(Path.Combine(localCacheFolder, Guid.NewGuid().ToString()), TraceFileExtension);
            _logger.LogDebug("Trace File Path: {traceFilePath}", _currentTraceFilePath);

            try
            {
                _logger.LogDebug("Call TraceControl.Enable().");
                _traceControl.Enable(_currentTraceFilePath);
                profilerStarted = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start eventpipe profiling.");
                throw;
            }

            _appInsightsSinks.LogInformation(StartProfilerSucceeded);

            Guid authenticatedUserId = await _serviceProfilerContext.GetAppInsightsAppIdAsync().ConfigureAwait(false);
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
            // Notice: Stop trace session listener has to be happen before calling ITraceControl.Disable().
            // Trace control disabling will take a while. We shall not gathering anything events when disable is happening.
            _logger.LogDebug("Disabling {sessionListener}", nameof(SessionListener));
            List<SampleActivity> sampleActivities = SessionListener?.SampleActivities?.GetActivities()?.ToList();

            SessionListener?.Dispose();
            SessionListener = null;

            try
            {
                await _traceControl.DisableAsync(cancellationToken).ConfigureAwait(false);

                profilerStopped = await _postStopProcessorFactory.Create().PostStopProcessAsync(new PostStopOptions(
                    _currentTraceFilePath,
                    currentSessionId.Value,
                    stampFrontendHostUrl: _serviceProfilerContext.StampFrontendEndpointUrl,
                    sampleActivities ?? Enumerable.Empty<SampleActivity>(),
                    source
                    ), cancellationToken).ConfigureAwait(false);

                _appInsightsSinks.LogInformation(profilerStopped ? StopProfilerSucceeded : StopProfilerFailed);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fail to disable EventPipe profiling.");
                _appInsightsSinks.LogInformation(StopProfilerFailed);
                throw;
            }
            finally
            {
                ReleaseSemaphoreForProfiling();
            }

            _logger.LogInformation(TelemetryConstants.SessionEnded);

            return profilerStopped;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error happens on stopping service profiler tracing.");
            throw;
        }
        finally
        {
            try
            {
                if (profilerStopped)
                {
                    ReleaseSemaphoreForProfiling();
                }
            }
            catch (SemaphoreFullException ex)
            {
                _logger.LogWarning(ex, "Additional releasing of semaphore upon stopping profiler. This should not happen very often. Please contact the project owner otherwise.");
            }
        }
    }

    private void ReleaseSemaphoreForProfiling()
    {
        if (IsProfilerRunning)
        {
            _singleProfilingSemaphore.Release();
        }
    }

    private void ActivateNext(object sender, EventArgs e)
    {
        if (SessionListener != null)
        {
            SessionListener.Poisoned -= ActivateNext;
            SessionListener.Dispose();
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

    public void Dispose()
    {
        _singleProfilingSemaphore?.Dispose();
        _singleProfilingSemaphore = null;

        _sessionListeners?.Dispose();
        _sessionListeners = null;
    }
}
