
namespace Azure.Monitor.OpenTelemetry.Profiler.Core;

internal class DumbTraceControl : ITraceControl
{
    public DateTime SessionStartUTC => throw new NotImplementedException();

    public Task DisableAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task EnableAsync(string traceFilePath = "default.nettrace", CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}