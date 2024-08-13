
using Azure.Monitor.OpenTelemetry.Profiler.Core.EventListeners;
using Microsoft.Extensions.Logging;

namespace Azure.Monitor.OpenTelemetry.Profiler.Core;

internal sealed class OpenTelemetryProfilerProvider : IServiceProfilerProvider, IDisposable
{
    private readonly ITraceControl _traceControl;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<OpenTelemetryProfilerProvider> _logger;

    private TraceSessionListener? _listener;

    public OpenTelemetryProfilerProvider(
        ITraceControl traceControl,
        ILoggerFactory loggerFactory,
        ILogger<OpenTelemetryProfilerProvider> logger)
    {
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _traceControl = traceControl ?? throw new ArgumentNullException(nameof(traceControl));
    }

    public async Task<bool> StartServiceProfilerAsync(IProfilerSource source, CancellationToken cancellationToken = default)
    {
        
        // TODO: use temp folder managaer instead.
        string traceFileFullPath = GetTempTraceFileName();
        // ~
        _logger.LogInformation("Starting profiling. Local trace file: {traceFileFullPath}", traceFileFullPath);
        await _traceControl.EnableAsync(traceFileFullPath, cancellationToken).ConfigureAwait(false);
        
        await Task.Delay(TimeSpan.FromSeconds(2));
        _listener = new TraceSessionListener(_loggerFactory.CreateLogger<TraceSessionListener>());

        return true;
    }

    public async Task<bool> StopServiceProfilerAsync(IProfilerSource source, CancellationToken cancellationToken = default)
    {
        await _traceControl.DisableAsync(cancellationToken).ConfigureAwait(false);
        _listener?.Dispose();
        return true;
    }

    private static string GetTempTraceFileName()
    {
        string tempPath = Path.GetTempPath();
        string profilerFolder = Path.Combine(tempPath, "OTelTraces");
        Directory.CreateDirectory(profilerFolder);

        string fileName = Guid.NewGuid().ToString("D");
        fileName = Path.ChangeExtension(fileName, ".nettrace");

        string fullTraceFileName = Path.GetFullPath(Path.Combine(profilerFolder, fileName));
        return fullTraceFileName;
    }

    public void Dispose()
    {
        _listener?.Dispose();
    }
}