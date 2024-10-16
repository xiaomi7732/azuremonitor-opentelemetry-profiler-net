namespace Azure.Monitor.OpenTelemetry.Profiler.Core;

public interface IServiceProfilerProvider
{
    Task<bool> StartServiceProfilerAsync(IProfilerSource source, CancellationToken cancellationToken = default);

    Task<bool> StopServiceProfilerAsync(IProfilerSource source, CancellationToken cancellationToken = default);
}
