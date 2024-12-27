// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Monitor.OpenTelemetry.Profiler.Core;
using Microsoft.ApplicationInsights.Profiler.Shared.Contracts;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.ServiceProfiler.Orchestration;

namespace Azure.Monitor.OpenTelemetry.Profiler.OOPHost;

internal sealed class OOPProfilerProvider : IServiceProfilerProvider, IProfilerSource, IDisposable
{
    internal const string TraceFileExtension = ".nettrace";
    private string? _currentTraceFilePath;
    private readonly SemaphoreSlim _singleProfilingSemaphore = new(1, 1);
    private bool IsSemaphoreTaken => _singleProfilingSemaphore.CurrentCount == 0;

    private readonly ITraceControl _traceControl;
    private readonly IUserCacheManager _userCacheManager;
    private readonly IPostStopProcessorFactory _postStopProcessorFactory;
    private readonly IServiceProfilerContext _serviceProfilerContext;
    private readonly ILogger _logger;

    public string Source => nameof(OOPProfilerProvider);

    public OOPProfilerProvider(
        ITraceControl traceControl,
        IUserCacheManager userCacheManager,
        IPostStopProcessorFactory postStopProcessorFactory,
        IServiceProfilerContext serviceProfilerContext,
        ILogger<OOPProfilerProvider> logger)
    {
        _userCacheManager = userCacheManager ?? throw new ArgumentNullException(nameof(userCacheManager));
        _postStopProcessorFactory = postStopProcessorFactory ?? throw new ArgumentNullException(nameof(postStopProcessorFactory));
        _serviceProfilerContext = serviceProfilerContext ?? throw new ArgumentNullException(nameof(serviceProfilerContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _traceControl = traceControl ?? throw new ArgumentNullException(nameof(traceControl));
    }

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

        string localCacheFolder = _userCacheManager.TempTraceDirectory.FullName;
        Directory.CreateDirectory(localCacheFolder);

        _currentTraceFilePath = Path.ChangeExtension(Path.Combine(localCacheFolder, Guid.NewGuid().ToString()), TraceFileExtension);
        _logger.LogDebug("Trace File Path: {traceFilePath}", _currentTraceFilePath);

        try
        {
            _logger.LogDebug("Call TraceControl.Enable().");
            await _traceControl.EnableAsync(_currentTraceFilePath, cancellationToken).ConfigureAwait(false);
            profilerStarted = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start eventpipe profiling.");
            throw;
        }

        return profilerStarted;
    }

    public async Task<bool> StopServiceProfilerAsync(IProfilerSource source, CancellationToken cancellationToken = default)
    {
        _logger.LogTrace("Entering {StopServiceProfilerAsync}.", nameof(StopServiceProfilerAsync));

        bool profilerStopped = false;

        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (!IsSemaphoreTaken)
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
            // Disable the EventPipe.
            int targetProcessId = await _traceControl.DisableAsync(cancellationToken).ConfigureAwait(false);
            profilerStopped = true;

            // When target process is zero, there's something wrong.
            if (targetProcessId == 0)
            {
                _logger.LogWarning("Target process id can't be zero.");
                return false;
            }

            ReleaseSemaphoreForProfiling();

            await _postStopProcessorFactory.Create().PostStopProcessAsync(new PostStopOptions(
                traceFilePath: _currentTraceFilePath,
                sessionId: currentSessionId.Value,
                stampFrontendHostUrl: _serviceProfilerContext.StampFrontendEndpointUrl,
                samples: Enumerable.Empty<SampleActivity>(), // TODO: abstract a sample provider?
                profilerSource: source,
                processId: targetProcessId), cancellationToken).ConfigureAwait(false);

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
            if (profilerStopped)
            {
                ReleaseSemaphoreForProfiling();
            }
        }
    }

    public void Dispose()
    {
        _singleProfilingSemaphore.Dispose();
    }

    private void ReleaseSemaphoreForProfiling()
    {
        if (IsSemaphoreTaken)
        {
            _singleProfilingSemaphore.Release();
        };
    }
}