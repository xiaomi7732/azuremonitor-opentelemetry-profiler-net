using System.Runtime.InteropServices;
using Microsoft.ApplicationInsights.Profiler.Shared.Services;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
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
        services.TryAddSingleton<IOrchestrator, OrchestrationImp>();
        // TODO: saars: Append specific schedulers
        // ~


        services.TryAddSingleton<ISerializationProvider, HighPerfJsonSerializationProvider>();
        services.TryAddTransient<IDelaySource, DefaultDelaySource>();

        // TODO: saars: Make this an transient service - won't be used afterwards:
        bool isRunningOnWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        if (isRunningOnWindows)
        {
            services.TryAddSingleton<INetCoreAppVersion, WindowsNetCoreAppVersion>();
        }
        else
        {
            services.TryAddSingleton<INetCoreAppVersion, LinuxNetCoreAppVersion>();
        }
        services.TryAddSingleton<IVersionProvider>(p => new VersionProvider(RuntimeInformation.FrameworkDescription, p.GetRequiredService<ILogger<IVersionProvider>>()));
        services.TryAddSingleton<ICompatibilityUtility, RuntimeCompatibilityUtility>();
        // ~

        return services;
    }
}