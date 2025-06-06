using Microsoft.ApplicationInsights.Profiler.Core.Contracts;
using Microsoft.ApplicationInsights.Profiler.Core.TraceScavenger;
using Microsoft.ApplicationInsights.Profiler.Shared.Orchestrations;
using Microsoft.ApplicationInsights.Profiler.Shared.Orchestrations.MetricsProviders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.ServiceProfiler.DataContract.Settings;
using Microsoft.ServiceProfiler.Orchestration;
using Microsoft.ServiceProfiler.Orchestration.MetricsProviders;
using Microsoft.ServiceProfiler.Utilities;
using System;
using System.Runtime.InteropServices;

namespace Microsoft.ApplicationInsights.Profiler.Core;

/// <summary>
/// Service Collection extensions for the Core project.
/// </summary>
/// <remarks>
/// Many services within the core have been registered in the header projects, such as Microsoft.ApplicationInsights.Profiler.AspNetCore.
/// Please consider migrating them back to this project to enhance the clarity of the architecture.
/// <remarks>
internal static class ServiceCollectionExtensions
{
    /// <summary>
    /// Register services provided in this core library.
    /// </summary>
    /// <param name="services"></param>
    public static IServiceCollection AddProfilerCoreServices(this IServiceCollection services)
    {
        // Customizations
        services.AddSingleton<ProfilerSettings>();
        services.AddSingleton<IProfilerSettingsService>(p =>
        {
            UserConfiguration userConfiguration = p.GetRequiredService<IOptions<UserConfiguration>>().Value;
            if (userConfiguration.StandaloneMode)
            {
                return ActivatorUtilities.CreateInstance<LocalProfileSettingsService>(p);
            }
            else
            {
                return ActivatorUtilities.CreateInstance<RemoteProfilerSettingsService>(p);
            }
        });

        // Triggers
        services.AddSingleton(_ => SettingsParser.Instance);
        services.AddSingleton<CpuTriggerSettings>();
        services.AddTransient<MemoryTriggerSettings>();

        services.AddKeyedSingleton<IMetricsProvider, ProcessInfoCPUMetricsProvider>(MetricsProviderCategory.CPU);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            services.AddKeyedSingleton<IMetricsProvider, WindowsMemoryMetricsProvider>(MetricsProviderCategory.Memory);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            services.AddSingleton<MemInfoItemParser>();
            services.AddSingleton<IMemInfoReader, ProcMemInfoReader>();
            services.AddKeyedSingleton<IMetricsProvider, MemInfoFileMemoryMetricsProvider>(MetricsProviderCategory.Memory);
        }
        else
        {
            throw new NotSupportedException($"Only support {OSPlatform.Windows} and {OSPlatform.Linux}.");
        }

        services.AddSingleton<IResourceUsageSource, ResourceUsageSource>();

        return services
            .AddSchedulers()
            .AddTraceScavengerServices();
    }

    private static IServiceCollection AddTraceScavengerServices(this IServiceCollection services)
    {
        // Register FileScavenger
        services.AddSingleton<IFileScavengerEventListener, TraceScavengerListener>();

        services.AddSingleton(CreateFileScavenger);

        services.AddHostedService<TraceScavengerService>();

        return services;
    }

    private static FileScavenger CreateFileScavenger(IServiceProvider serviceProvider)
    {
        UserConfiguration configuration = serviceProvider.GetRequiredService<IOptions<UserConfiguration>>().Value;
        IUserCacheManager cacheManager = serviceProvider.GetRequiredService<IUserCacheManager>();

        return ActivatorUtilities.CreateInstance<FileScavenger>(serviceProvider,
            new FileScavengerOptions(cacheManager.TempTraceDirectory.FullName)
            {
                DeletePattern = "*" + ServiceProfilerProvider.TraceFileExtension, // *.netperf
                GracePeriod = configuration.TraceScavenger.GracePeriod,
            });
    }

    private static IServiceCollection AddSchedulers(this IServiceCollection services)
    {
        services.AddSingleton<ProcessExpirationPolicy>();
        services.AddSingleton<LimitedExpirationPolicyFactory>();

        services.AddSingleton<IOrchestrator, OrchestrationImp>();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<SchedulingPolicy, OneTimeSchedulingPolicy>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<SchedulingPolicy, RandomSchedulingPolicy>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<SchedulingPolicy, OnDemandSchedulingPolicy>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<SchedulingPolicy, MemoryMonitoringSchedulingPolicy>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<SchedulingPolicy, CPUMonitoringSchedulingPolicy>());

        return services;
    }
}
