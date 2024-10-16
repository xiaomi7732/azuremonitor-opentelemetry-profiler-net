using Microsoft.ApplicationInsights.Profiler.Shared.Services;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Orchestrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.ServiceProfiler.Orchestration;

namespace Azure.Monitor.OpenTelemetry.Profiler.Core;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddServiceProfilerCore(this IServiceCollection services)
    {
        services.TryAddSingleton(_ => DiagnosticsClientProvider.Instance);
        services.TryAddSingleton<ITraceControl, DumbTraceControl>();
        services.TryAddSingleton<IServiceProfilerProvider, OpenTelemetryProfilerProvider>();

        services.TryAddSingleton<IServiceProfilerContext, StubServiceProfilerContext>();
        services.TryAddSingleton<IOrchestrator, OrchestratorEventPipe>();
        // TODO: saars: Append specific schedulers
        // ~
        
       
        // TODO: saars: Make this an transient service - won't be used afterwards:
        services.TryAddSingleton<ICompatibilityUtility, RuntimeCompatibilityUtility>();
        // ~

        services.TryAddSingleton<ISerializationProvider, >();

        return services;
    }
}