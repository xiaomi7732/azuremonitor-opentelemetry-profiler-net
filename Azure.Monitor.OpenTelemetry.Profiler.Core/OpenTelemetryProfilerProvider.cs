
using Microsoft.Extensions.Logging;

namespace Azure.Monitor.OpenTelemetry.Profiler.Core;

internal class OpenTelemetryProfilerProvider : IServiceProfilerProvider
{
    private readonly ITraceControl _traceControl;
    private readonly ILogger<OpenTelemetryProfilerProvider> _logger;

    public OpenTelemetryProfilerProvider(
        ITraceControl traceControl,
        ILogger<OpenTelemetryProfilerProvider> logger)
    {
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

        return true;
    }

    public async Task<bool> StopServiceProfilerAsync(IProfilerSource source, CancellationToken cancellationToken = default)
    {
        await _traceControl.DisableAsync(cancellationToken).ConfigureAwait(false);
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
}