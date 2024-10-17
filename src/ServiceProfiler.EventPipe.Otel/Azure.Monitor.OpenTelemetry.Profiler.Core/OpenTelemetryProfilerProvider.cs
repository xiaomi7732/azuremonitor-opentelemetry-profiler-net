
using Azure.Monitor.OpenTelemetry.Profiler.Core.EventListeners;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ServiceProfiler.Orchestration;

namespace Azure.Monitor.OpenTelemetry.Profiler.Core;

internal sealed class OpenTelemetryProfilerProvider : IServiceProfilerProvider, IProfilerSource, IDisposable
{
    private const string TraceFileExtension = ".nettrace";
    private string? _currentTraceFilePath;
    private readonly SemaphoreSlim _singleProfilingSemaphore = new(1, 1);
    private bool IsSemaphoreTaken => _singleProfilingSemaphore.CurrentCount == 0;

    private readonly ITraceControl _traceControl;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IUserCacheManager _userCacheManager;
    private readonly TraceSessionListenerFactory _traceSessionListenerFactory;
    private readonly ServiceProfilerOptions _options;
    private readonly ILogger<OpenTelemetryProfilerProvider> _logger;

    private TraceSessionListener? _listener;

    public string Source => nameof(OpenTelemetryProfilerProvider);

    public OpenTelemetryProfilerProvider(
        ITraceControl traceControl,
        ILoggerFactory loggerFactory,
        IOptions<ServiceProfilerOptions> options,
        IUserCacheManager userCacheManager,
        TraceSessionListenerFactory traceSessionListenerFactory,
        ILogger<OpenTelemetryProfilerProvider> logger)
    {
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _userCacheManager = userCacheManager ?? throw new ArgumentNullException(nameof(userCacheManager));
        _traceSessionListenerFactory = traceSessionListenerFactory ?? throw new ArgumentNullException(nameof(traceSessionListenerFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _traceControl = traceControl ?? throw new ArgumentNullException(nameof(traceControl));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
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

        try
        {
            await _traceControl.DisableAsync(cancellationToken).ConfigureAwait(false);
            profilerStopped = true;

            _listener?.Dispose();
            ReleaseSemaphoreForProfiling();

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fail to disable EventPipe profiling.");
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
        _listener?.Dispose();
    }

    private void ReleaseSemaphoreForProfiling()
    {
        if (IsSemaphoreTaken)
        {
            _singleProfilingSemaphore.Release();
        };
    }
}