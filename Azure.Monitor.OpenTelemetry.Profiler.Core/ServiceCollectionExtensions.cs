using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Azure.Monitor.OpenTelemetry.Profiler.Core;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddServiceProfilerCore(this IServiceCollection services)
    {
        services.TryAddSingleton(_ => DiagnosticsClientProvider.Instance);
        services.TryAddSingleton<ITraceControl, DumbTraceControl>();
        services.TryAddSingleton<IServiceProfilerProvider, OpenTelemetryProfilerProvider>();

        return services;
    }
}