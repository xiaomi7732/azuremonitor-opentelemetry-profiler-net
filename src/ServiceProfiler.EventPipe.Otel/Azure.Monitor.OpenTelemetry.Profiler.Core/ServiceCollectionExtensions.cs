//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using Azure.Monitor.OpenTelemetry.Profiler.Core.Contracts;
using Azure.Monitor.OpenTelemetry.Profiler.Core.EventListeners;
using Azure.Monitor.OpenTelemetry.Profiler.Core.Orchestrations;
using Microsoft.ApplicationInsights.Profiler.Core.Utilities;
using Microsoft.ApplicationInsights.Profiler.Shared.Samples;
using Microsoft.ApplicationInsights.Profiler.Shared.Services;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions.Auth;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions.IPC;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.IPC;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Orchestrations;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.TraceScavenger;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.UploaderProxy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.ServiceProfiler.Orchestration;
using Microsoft.ServiceProfiler.Utilities;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Azure.Monitor.OpenTelemetry.Profiler.Core;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddServiceProfilerCore(this IServiceCollection services)
    {
        // Utilities
        services.AddSingleton<IConnectionStringParserFactory, ConnectionStringParserFactory>();
        services.AddSingleton<IRoleNameSource, EnvRoleName>();


        services.AddSingleton<IFile, SystemFile>();
        services.AddSingleton<IEnvironment, SystemEnvironment>();
        services.AddSingleton<IZipFile, SystemZipFile>();
        services.AddSingleton<IDelaySource, DefaultDelaySource>();
        services.AddSingleton<IRandomSource, DefaultRandomSource>();

        services.AddSingleton<IProfilerCoreAssemblyInfo>(_ => ProfilerCoreAssemblyInfo.Instance);
        services.AddSingleton<IUserCacheManager, UserCacheManager>();

        services.AddSingleton<IPayloadSerializer, HighPerfJsonSerializationProvider>();
        services.AddSingleton<ISerializationProvider, HighPerfJsonSerializationProvider>();
        services.AddSingleton<ISerializationOptionsProvider<JsonSerializerOptions>, HighPerfJsonSerializationProvider>();
        services.TryAddSingleton<ISerializationProvider, HighPerfJsonSerializationProvider>();

        // Uploader caller
        AddUploaderCallerServices(services);

        // Named pipe client
        services.TryAddSingleton<INamedPipeClientFactory, NamedPipeClientFactory>();

        // Profiler Context
        services.TryAddSingleton<IEndpointProvider, EndpointProvider>();
        services.TryAddTransient<IMetadataWriter, MetadataWriter>();

        // Transient trace session listeners
        services.TryAddTransient<SampleActivityContainer>();
        services.TryAddTransient<SampleCollector>();
        services.TryAddSingleton<TraceSessionListenerFactory>();

        // Profiler
        services.TryAddTransient<ICustomEventsBuilder, CustomEventsBuilder>();
        services.TryAddSingleton<IPostStopProcessorFactory, PostStopProcessorFactory>();
        services.TryAddSingleton(_ => DiagnosticsClientProvider.Instance);
        services.TryAddSingleton<ITraceControl, DumbTraceControl>();
        services.TryAddSingleton<IServiceProfilerContext, ServiceProfilerContext>();
        services.TryAddSingleton<IServiceProfilerProvider, OpenTelemetryProfilerProvider>();

        // Token
        services.AddSingleton<IAuthTokenProvider, AuthTokenProvider>();

        // Orchestrator
        AddSchedulers(services);

        // Compatibility test
        bool isRunningOnWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        if (isRunningOnWindows)
        {
            services.AddTransient<INetCoreAppVersion, WindowsNetCoreAppVersion>();
        }
        else
        {
            services.AddTransient<INetCoreAppVersion, LinuxNetCoreAppVersion>();
        }
        services.AddTransient<IVersionProvider>(p => ActivatorUtilities.CreateInstance<VersionProvider>(p, RuntimeInformation.FrameworkDescription));
        services.AddSingleton<ICompatibilityUtilityFactory, RuntimeCompatibilityUtilityFactory>();
        // ~

        // Customizations
        services.AddSingleton<ProfilerSettings>();
        services.AddSingleton<IProfilerSettingsService>(p =>
        {
            ServiceProfilerOptions userConfiguration = p.GetRequiredService<IOptions<ServiceProfilerOptions>>().Value;

            if (userConfiguration.StandaloneMode)
            {
                return ActivatorUtilities.CreateInstance<LocalProfileSettingsService>(p);
            }
            else
            {
                return ActivatorUtilities.CreateInstance<RemoteSettingsService>(p);
            }
        });

        // Triggers
        services.TryAddSingleton<IResourceUsageSource, StubResourceUsageSource>();

        // Scavengers
        AddTraceScavengerServices(services);

        return services;
    }

    private static void AddUploaderCallerServices(IServiceCollection services)
    {
        services.AddTransient<IUploadContextValidator, UploadContextValidator>();

        services.AddTransient<IPrioritizedUploaderLocator, UploaderLocatorByEnvironmentVariable>();
        services.AddTransient<IPrioritizedUploaderLocator, UploaderLocatorInUserCache>();
        services.AddTransient<IPrioritizedUploaderLocator, UploaderLocatorByUnzipping>();
        services.AddTransient<IUploaderPathProvider, UploaderPathProvider>();

        services.AddSingleton<IOutOfProcCallerFactory, OutOfProcCallerFactory>();

        services.AddTransient<ITraceUploader, TraceUploaderProxy>();
    }

    private static void AddSchedulers(IServiceCollection services)
    {
        services.AddSingleton<ProcessExpirationPolicy>();
        services.AddSingleton<LimitedExpirationPolicyFactory>();

        services.TryAddSingleton<IOrchestrator, OrchestrationImp>();

        // TODO: saars: Append specific schedulers
        services.TryAddEnumerable(ServiceDescriptor.Singleton<SchedulingPolicy, OneTimeSchedulingPolicy>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<SchedulingPolicy, RandomSchedulingPolicy>());
        // ~
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
        ServiceProfilerOptions configuration = serviceProvider.GetRequiredService<IOptions<ServiceProfilerOptions>>().Value;
        IUserCacheManager cacheManager = serviceProvider.GetRequiredService<IUserCacheManager>();

        return ActivatorUtilities.CreateInstance<FileScavenger>(serviceProvider,
            new FileScavengerOptions(cacheManager.TempTraceDirectory.FullName)
            {
                DeletePattern = "*" + OpenTelemetryProfilerProvider.TraceFileExtension, // => "*.nettrace"
                GracePeriod = configuration.TraceScavenger.GracePeriod,
            });
    }
}