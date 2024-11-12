using System.Runtime.InteropServices;
using Azure.Monitor.OpenTelemetry.Profiler.Core.Contracts;
using Azure.Monitor.OpenTelemetry.Profiler.Core.EventListeners;
using Azure.Monitor.OpenTelemetry.Profiler.Core.Orchestrations;
using Microsoft.ApplicationInsights.Profiler.Shared;
using Microsoft.ApplicationInsights.Profiler.Shared.Samples;
using Microsoft.ApplicationInsights.Profiler.Shared.Services;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions.Auth;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Orchestrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ServiceProfiler.Orchestration;

namespace Azure.Monitor.OpenTelemetry.Profiler.Core;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddServiceProfilerCore(this IServiceCollection services)
    {
        services.AddSharedServices();

        services.TryAddSingleton(_ => DiagnosticsClientProvider.Instance);

        services.TryAddSingleton<IProfilerCoreAssemblyInfo>(_ => ProfilerCoreAssemblyInfo.Instance);
        services.TryAddSingleton<IUserCacheManager, UserCacheManager>();
        services.TryAddSingleton<ITraceControl, DumbTraceControl>();

        // Transient trace session listeners
        services.TryAddTransient<SampleActivityContainer>();
        services.TryAddTransient<SampleCollector>();
        services.TryAddSingleton<TraceSessionListenerFactory>();

        services.AddSingleton<IProfilerFrontendClientFactory, ProfilerFrontendClientFactory>();
        services.AddSingleton<ITraceUploader, TraceUploaderProxy>();

        services.AddSingleton<IAuthTokenProvider, AuthTokenProvider>();

        services.TryAddTransient<ICustomEventsBuilder, CustomEventsBuilder>();
        services.TryAddTransient<IPostStopProcessorFactory, PostStopProcessorFactory>();

        services.TryAddSingleton<IServiceProfilerProvider, OpenTelemetryProfilerProvider>();

        services.TryAddSingleton<IServiceProfilerContext, ServiceProfilerContext>();
        
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

        services.AddSingleton<ProfilerSettings>();
        services.TryAddSingleton<IProfilerSettingsService>(p =>
        {
            ServiceProfilerOptions userConfiguration = p.GetRequiredService<IOptions<ServiceProfilerOptions>>().Value;

            if (userConfiguration.StandaloneMode)
            {
                return ActivatorUtilities.CreateInstance<LocalProfileSettingsService>(p);
            }
            // TODO: implement this later
            // else
            // {
            //     return ActivatorUtilities.CreateInstance<RemoteProfilerSettingsService>(p);
            // }
            throw new NotImplementedException("Settings other than local is not implemented.");
        });

        services.TryAddSingleton<IResourceUsageSource, StubResourceUsageSource>();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<SchedulingPolicy, OneTimeSchedulingPolicy>());

        return services;
    }
}