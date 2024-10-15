namespace Azure.Monitor.OpenTelemetry.Profiler.AspNetCore;

public class ServiceProfilerOptions
{
    public TimeSpan Duration { get; set; } = TimeSpan.FromSeconds(30);
}