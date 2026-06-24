//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using Azure.Monitor.OpenTelemetry.Profiler.Core.EventListeners;
using Microsoft.ApplicationInsights.Profiler.Shared.Contracts;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.ServiceProfiler.Orchestration;

namespace Azure.Monitor.OpenTelemetry.Profiler.Core;

internal sealed class OpenTelemetryProfilerProvider : IServiceProfilerProvider, IProfilerSource, IDisposable
{
    internal const string TraceFileExtension = ".nettrace";
    private string? _currentTraceFilePath;
    private float _sessionCPUUsage;
    private float _sessionMemoryUsage;
    private readonly SemaphoreSlim _singleProfilingSemaphore = new(1, 1);
    private readonly ITraceControl _traceControl;
    private readonly IUserCacheManager _userCacheManager;
    private readonly TraceSessionListenerFactory _traceSessionListenerFactory;
    private readonly IPostStopProcessorFactory _postStopProcessorFactory;
    private readonly IServiceProfilerContext _serviceProfilerContext;
    private readonly IResourceUsageSource _resourceUsageSource;
    private readonly ILogger<OpenTelemetryProfilerProvider> _logger;

    private const string StartProfilerTriggered = "StartProfiler triggered.";
    private const string StartProfilerSucceeded = "StartProfiler succeeded.";
    private const string StopProfilerTriggered = "StopProfiler triggered.";
    private const string StopProfilerSucceeded = "StopProfiler succeeded.";

    private TraceSessionListener? _listener;

    /// <inheritdoc />
    public bool IsProfilerRunning => _singleProfilingSemaphore.CurrentCount == 0;
    public string Source => nameof(OpenTelemetryProfilerProvider);

    public OpenTelemetryProfilerProvider(
        ITraceControl traceControl,
        IUserCacheManager userCacheManager,
        TraceSessionListenerFactory traceSessionListenerFactory,
        IPostStopProcessorFactory postStopProcessorFactory,
        IServiceProfilerContext serviceProfilerContext,
        IResourceUsageSource resourceUsageSource,
        ILogger<OpenTelemetryProfilerProvider> logger)
    {
        _userCacheManager = userCacheManager ?? throw new ArgumentNullException(nameof(userCacheManager));
        _traceSessionListenerFactory = traceSessionListenerFactory ?? throw new ArgumentNullException(nameof(traceSessionListenerFactory));
        _postStopProcessorFactory = postStopProcessorFactory ?? throw new ArgumentNullException(nameof(postStopProcessorFactory));
        _serviceProfilerContext = serviceProfilerContext ?? throw new ArgumentNullException(nameof(serviceProfilerContext));
        _resourceUsageSource = resourceUsageSource ?? throw new ArgumentNullException(nameof(resourceUsageSource));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _traceControl = traceControl ?? throw new ArgumentNullException(nameof(traceControl));
    }

    /// <inheritdoc />
    public async Task<bool> StartServiceProfilerAsync(IProfilerSource source, CancellationToken cancellationToken = default)
    {
        _logger.LogTrace("Entering {name}.", nameof(StartServiceProfilerAsync));

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

        // Successfully acquired semaphore here.
        bool profilerStarted = false;

        _logger.LogTrace("Got the semaphore. Try starting the Profiler.");
        _logger.LogInformation(StartProfilerTriggered);

        string localCacheFolder = _userCacheManager.TempTraceDirectory.FullName;
        Directory.CreateDirectory(localCacheFolder);

        _currentTraceFilePath = Path.ChangeExtension(Path.Combine(localCacheFolder, Guid.NewGuid().ToString()), TraceFileExtension);
        _logger.LogDebug("Trace File Path: {traceFilePath}", _currentTraceFilePath);

        try
        {
            // Capture resource usage at the beginning of the profiling session, before trace collection starts.
            _sessionCPUUsage = _resourceUsageSource.GetAverageCPUUsage();
            _sessionMemoryUsage = _resourceUsageSource.GetAverageMemoryUsage();
            _logger.LogDebug("Captured resource usage at session start: CPU={cpuUsage}, Memory={memoryUsage}", _sessionCPUUsage, _sessionMemoryUsage);

            _logger.LogDebug("Call TraceControl.Enable().");
            await _traceControl.EnableAsync(_currentTraceFilePath, cancellationToken).ConfigureAwait(false);

            // Dispose any previous trace session listener
            _listener?.Dispose();
            _listener = _traceSessionListenerFactory.Create();
            _logger.LogDebug("New traceSessionListener created.");

            profilerStarted = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start eventpipe profiling.");
            throw;
        }

        if (profilerStarted)
        {
            _logger.LogInformation(StartProfilerSucceeded);
        }

        return profilerStarted;
    }

    /// <inheritdoc />
    public async Task<bool> StopServiceProfilerAsync(IProfilerSource source, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(StopProfilerTriggered);
        _logger.LogTrace("Entering {StopServiceProfilerAsync}.", nameof(StopServiceProfilerAsync));

        bool profilerStopped = false;
        bool semaphoreReleased = false;

        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

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

        if (string.IsNullOrEmpty(_currentTraceFilePath))
        {
            throw new InvalidOperationException("Current trace file path can't be null.");
        }

        try
        {
            // Copy values captured at session start before releasing the semaphore, to avoid a race
            // where a new session could overwrite these fields once the semaphore is released below.
            float cpuUsage = _sessionCPUUsage;
            float memoryUsage = _sessionMemoryUsage;
            string currentTraceFilePath = _currentTraceFilePath;

            // Notice: Stop trace session listener has to be happen before calling ITraceControl.Disable().
            // Trace control disabling will take a while. We shall not gathering anything events when disable is happening.
            _logger.LogDebug("Disabling {sessionListener}", nameof(_listener));

            List<SampleActivity>? sampleActivities = _listener?.SampleActivities?.GetActivities()?.ToList();
            _listener?.Dispose();

            // Disable the EventPipe.
            await _traceControl.DisableAsync(cancellationToken).ConfigureAwait(false);
            profilerStopped = true;
            // Release the semaphore as soon as the trace is disabled so a new session can start
            // while the (potentially long) post-stop processing/upload runs. Mark it as released in
            // a finally so that even if Release() throws (e.g. the semaphore was disposed during
            // shutdown), the outer finally does not attempt a second release - that would throw
            // again and mask the original outcome, or free a semaphore acquired by a newer session.
            try
            {
                ReleaseSemaphoreForProfiling();
            }
            finally
            {
                semaphoreReleased = true;
            }

            // Only the post-stop processing resolves services from the (possibly disposed during
            // shutdown) root IServiceProvider, so scope the ObjectDisposedException handling to just
            // this call. An ObjectDisposedException from anywhere else (e.g. DisableAsync) must still
            // propagate so the caller learns the stop failed and the semaphore handling stays correct.
            try
            {
                await _postStopProcessorFactory.Create().PostStopProcessAsync(new PostStopOptions(
                    currentTraceFilePath,
                    currentSessionId.Value,
                    stampFrontendHostUrl: _serviceProfilerContext.StampFrontendEndpointUrl,
                    sampleActivities ?? Enumerable.Empty<SampleActivity>(),
                    source,
                    averageCPUUsage: cpuUsage,
                    averageMemoryUsage: memoryUsage), cancellationToken).ConfigureAwait(false);
            }
            catch (ObjectDisposedException ex)
            {
                // The root IServiceProvider can be disposed while a profiling session is being stopped
                // during host shutdown (the post-stop processor is resolved via the service provider).
                // In that case there is nothing more we can do - the upload cannot proceed once the
                // container is torn down - so log it and return gracefully instead of a noisy error.
                _logger.LogWarning(ex, "Service provider was disposed (likely during host shutdown) before post-stop processing finished. Skipping trace upload.");
                return false;
            }

            _logger.LogInformation(StopProfilerSucceeded);

            return true;
        }
        catch (Exception ex)
        {
            if (!profilerStopped)
            {
                _logger.LogError(ex, "Fail to disable EventPipe profiling.");
            }
            else
            {
                _logger.LogError(ex, "Unexpected error after EventPipe profiler disabled.");
            }
            throw;
        }
        finally
        {
            if (profilerStopped && !semaphoreReleased)
            {
                ReleaseSemaphoreForProfiling();
            }
        }
    }

    public void Dispose()
    {
        _singleProfilingSemaphore.Dispose();
        _listener?.Dispose();
    }

    private void ReleaseSemaphoreForProfiling()
    {
        if (IsProfilerRunning)
        {
            _singleProfilingSemaphore.Release();
        }
    }
}