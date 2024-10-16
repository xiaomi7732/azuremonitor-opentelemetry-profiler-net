using Microsoft.ServiceProfiler.Orchestration;

namespace Azure.Monitor.OpenTelemetry.Profiler.AspNetCore;

public sealed class UnknownProfilerSource : IProfilerSource
{
    private UnknownProfilerSource()
    {
    }
    public static UnknownProfilerSource Instance { get; } = new UnknownProfilerSource();
    public string Source => nameof(UnknownProfilerSource);
}