//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using Microsoft.ApplicationInsights.Profiler.AspNetCore;
using Microsoft.ApplicationInsights.Profiler.Core;
using Microsoft.ApplicationInsights.Profiler.Core.Auth;
using Microsoft.ApplicationInsights.Profiler.Core.Contracts;
using Microsoft.ApplicationInsights.Profiler.Core.EventListeners;
using Microsoft.ApplicationInsights.Profiler.Core.IPC;
using Microsoft.ApplicationInsights.Profiler.Core.Logging;
using Microsoft.ApplicationInsights.Profiler.Core.Orchestration;
using Microsoft.ApplicationInsights.Profiler.Core.Orchestration.MetricsProviders;
using Microsoft.ApplicationInsights.Profiler.Core.SampleTransfers;
using Microsoft.ApplicationInsights.Profiler.Core.Sampling;
using Microsoft.ApplicationInsights.Profiler.Core.TraceControls;
using Microsoft.ApplicationInsights.Profiler.Core.UploaderProxy;
using Microsoft.ApplicationInsights.Profiler.Core.Utilities;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ServiceProfiler.Agent.FrontendClient;
using Microsoft.ServiceProfiler.DataContract.Settings;
using Microsoft.ServiceProfiler.Orchestration;
using Microsoft.ServiceProfiler.Orchestration.MetricsProviders;
using Microsoft.ServiceProfiler.Utilities;
using ServiceProfiler.EventPipe.Logging;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Microsoft.Extensions.DependencyInjection
{
    internal class ServiceCollectionBuilder : IServiceCollectionBuilder
    {
        public IServiceCollection Build(IServiceCollection serviceCollection)
        {
            if (serviceCollection is null)
            {
                throw new ArgumentNullException(nameof(serviceCollection));
            }

            // In AppInsights code, there is a check to ensure not inject the service twice:
            // Reference: https://github.com/Microsoft/ApplicationInsights-aspnetcore/blob/3dcab5b92ebddc92e9010fc707cc7062d03f92e4/src/Microsoft.ApplicationInsights.AspNetCore/Extensions/ApplicationInsightsExtensions.cs
            serviceCollection.AddApplicationInsightsTelemetry();

            // Telemetry

            // AI for Microsoft depends on user settings.
            serviceCollection.AddSingleton<IAppInsightsLogger>(provider =>
            {
                UserConfiguration userConfiguration = provider.GetRequiredService<IOptions<UserConfiguration>>().Value;
                ILogger logger = provider.GetRequiredService<ILogger<IAppInsightsLogger>>();

                if (userConfiguration.ProvideAnonymousTelemetry)
                {
                    logger.LogDebug("Sending anonymous telemetry data to Microsoft to make this product better.");
                    return new EventPipeAppInsightsLogger(
                        TelemetryConstants.ServiceProfilerAgentIKey);
                }
                else
                {
                    logger.LogDebug("No anonymous telemetry data is sent to Microsoft.");
                    return new NullAppInsightsLogger();
                }
            });

            // AI for the customer.
            serviceCollection.AddSingleton<IAppInsightsLogger>(p =>
            {
                return new EventPipeAppInsightsLogger(
                    p.GetRequiredService<IServiceProfilerContext>().AppInsightsInstrumentationKey);
            });

            // Consume IEnumerable<IAppInsightsLogger> to form a sink.
            serviceCollection.TryAddSingleton<IAppInsightsSinks, AppInsightsSinks>();

            // Specific trackers for customEvents in profiling.
            serviceCollection.TryAddSingleton<ICustomTelemetryClientFactory, CustomTelemetryClientFactory>();
            serviceCollection.TryAddSingleton<IEventPipeTelemetryTracker, TelemetryTracker>();
            serviceCollection.AddHostedService<TelemetryTrackerBackgroundService>();

            // Configurations
            serviceCollection.TryAddSingleton<IEndpointProvider, EndpointProviderMirror>();

            serviceCollection.TryAddSingleton<AppInsightsProfileFetcher>(p =>
            {
                var endpointProvider = p.GetRequiredService<IEndpointProvider>();
                var instance = new AppInsightsProfileFetcher(breezeEndpoint: endpointProvider.GetEndpoint(EndpointName.IngestionEndpoint).AbsoluteUri);
                return instance;
            });

            serviceCollection.TryAddSingleton<IServiceProfilerContext, ServiceProfilerContext>();

            // Register trace configuration
            serviceCollection.TryAddSingleton<DiagnosticsClientTraceConfiguration>();

            // Dependencies
            RegisterAppInsightsAADAuthServices(serviceCollection);
            RegisterFrontendClient(serviceCollection);

            // Core components
            serviceCollection.TryAddSingleton<IServiceProfilerProvider, ServiceProfilerProvider>();

            serviceCollection.TryAddSingleton<SampleActivityContainerFactory>();

            serviceCollection.AddTransient<IPrioritizedUploaderLocator, UploaderLocatorByEnvironmentVariable>();
            serviceCollection.AddTransient<IPrioritizedUploaderLocator, UploaderLocatorInUserCache>();
            serviceCollection.AddTransient<IPrioritizedUploaderLocator, UploaderLocatorByUnzipping>();
            serviceCollection.TryAddTransient<IUploaderPathProvider, UploaderPathProvider>();

            serviceCollection.TryAddSingleton(_ => DiagnosticsClientProvider.Instance);

            RegisterTraceControls(serviceCollection);

            serviceCollection.TryAddSingleton<ITraceSessionListenerFactory, TraceSessionListenerFactory>();
            serviceCollection.TryAddSingleton<CustomEventsTracker>();
            serviceCollection.TryAddSingleton<ICustomEventsTracker>(p => p.GetRequiredService<CustomEventsTracker>());
            serviceCollection.TryAddSingleton<IRoleNameSource>(p => p.GetRequiredService<CustomEventsTracker>());
            serviceCollection.TryAddSingleton<IOutOfProcCaller, OutOfProcCaller>();
            serviceCollection.TryAddSingleton<IFile, SystemFile>();
            serviceCollection.TryAddSingleton<IZipFile, SystemZipFile>();
            serviceCollection.TryAddSingleton<IEnvironment, SystemEnvironment>();
            serviceCollection.TryAddSingleton<IMetadataWriter, MetadataWriter>();

            bool isRunningOnWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            if (isRunningOnWindows)
            {
                serviceCollection.TryAddSingleton<INetCoreAppVersion, WindowsNetCoreAppVersion>();
            }
            else
            {
                serviceCollection.TryAddSingleton<INetCoreAppVersion, LinuxNetCoreAppVersion>();
            }

            serviceCollection.TryAddSingleton<IVersionProvider>(p => new VersionProvider(RuntimeInformation.FrameworkDescription, p.GetRequiredService<ILogger<IVersionProvider>>()));
            serviceCollection.TryAddSingleton<ICompatibilityUtility, RuntimeCompatibilityUtility>();

            serviceCollection.TryAddSingleton<IProfilerCoreAssemblyInfo>(p => ProfilerCoreAssemblyInfo.Instance);
            serviceCollection.TryAddTransient<IUserCacheManager, UserCacheManager>();

            // Named pipe client
            serviceCollection.TryAddTransient<ISerializationProvider, HighPerfJsonSerializationProvider>();
            serviceCollection.TryAddTransient<IPayloadSerializer, HighPerfJsonSerializationProvider>();
            serviceCollection.TryAddTransient<ISerializationOptionsProvider<JsonSerializerOptions>, HighPerfJsonSerializationProvider>();
            serviceCollection.TryAddSingleton<INamedPipeClientFactory, NamedPipeClientFactory>();

            // Registration for orchestrator and it's dependencies
            serviceCollection.TryAddSingleton<ITraceUploader>(p =>
            {
                UserConfiguration userConfiguration = p.GetRequiredService<IOptions<UserConfiguration>>().Value;
                if (userConfiguration.StandaloneMode)
                {
                    return ActivatorUtilities.CreateInstance<TraceUploaderNoServer>(p);
                }
                else
                {
                    return ActivatorUtilities.CreateInstance<TraceUploaderProxy>(p);
                }
            });

            serviceCollection.AddSingleton<ProfilerSettings>();
            serviceCollection.TryAddSingleton<IProfilerSettingsService>(p =>
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

            serviceCollection.TryAddSingleton<SettingsParser>(p => SettingsParser.Instance);
            serviceCollection.TryAddTransient<CpuTriggerSettings>();
            serviceCollection.TryAddTransient<MemoryTriggerSettings>();

            // Performance counters are not supported on non-Windows platforms.
            if (isRunningOnWindows)
            {
                // Metrics provider for CPU
                serviceCollection.TryAddSingleton<ProcessInfoCPUMetricsProvider>();

                // Metrics provider for Memory usage rate
                serviceCollection.TryAddTransient<WindowsMemoryMetricsProvider>();

                // Metrics provider resolver
                serviceCollection.TryAddTransient<IMetricsProviderResolver<MetricsProviderCategory>, WindowsMetricsProviderResolver>();
            }
            else
            {
                // Metrics Provider for CPU
                serviceCollection.TryAddSingleton<ProcessInfoCPUMetricsProvider>();

                // Metrics Provider for Memory
                serviceCollection.TryAddTransient<MemInfoItemParser>();
                serviceCollection.TryAddSingleton<IMemInfoReader, ProcMemInfoReader>();
                serviceCollection.TryAddSingleton<MemInfoFileMemoryMetricsProvider>();

                // Metrics provider resolver
                serviceCollection.TryAddTransient<IMetricsProviderResolver<MetricsProviderCategory>, MetricsProviderResolver>();

            }
            // Metrics provider consumer, works both on Windows and Linux.
            serviceCollection.TryAddSingleton<IResourceUsageSource, ResourceUsageSource>();

            serviceCollection.TryAddTransient<IRandomSource, DefaultRandomSource>();
            serviceCollection.TryAddTransient<IDelaySource, DefaultDelaySource>();

            serviceCollection.TryAddTransient<IExpirationPolicy, ProcessExpirationPolicy>();

            serviceCollection.TryAddEnumerable(ServiceDescriptor.Singleton<SchedulingPolicy, OneTimeSchedulingPolicy>());
            serviceCollection.TryAddEnumerable(ServiceDescriptor.Singleton<SchedulingPolicy, RandomSchedulingPolicy>());
            serviceCollection.TryAddEnumerable(ServiceDescriptor.Singleton<SchedulingPolicy, OnDemandSchedulingPolicy>());
            serviceCollection.TryAddEnumerable(ServiceDescriptor.Singleton<SchedulingPolicy, MemoryMonitoringSchedulingPolicy>());
            serviceCollection.TryAddEnumerable(ServiceDescriptor.Singleton<SchedulingPolicy, CPUMonitoringSchedulingPolicy>());

            serviceCollection.TryAddSingleton<IOrchestrator, OrchestratorEventPipe>();

            serviceCollection.TryAddTransient<IUploadContextValidator, UploadContextValidator>();

            // Add Service Profiler Background Service
            if (!serviceCollection.Any(descriptor =>
                descriptor.ImplementationType == typeof(ServiceProfilerBackgroundService)))
            {
                serviceCollection.TryAddSingleton<IServiceProfilerAgentBootstrap>(p =>
                {
                    UserConfiguration userConfiguration = p.GetRequiredService<IOptions<UserConfiguration>>().Value;
                    // Choose one by configurations to register.
                    return userConfiguration.IsDisabled ?
                        ActivatorUtilities.CreateInstance<DisabledAgentBootstrap>(p) :
                        ActivatorUtilities.CreateInstance<ServiceProfilerAgentBootstrap>(p);
                });
                serviceCollection.AddHostedService<ServiceProfilerBackgroundService>();
            }

            // Other core services.
            serviceCollection.AddProfilerCoreServices();

            return serviceCollection;
        }

        // Register services related to Application Insights AAD Auth
        private void RegisterAppInsightsAADAuthServices(IServiceCollection serviceCollection)
        {
            serviceCollection.TryAddSingleton<IAccessTokenFactory, AccessTokenFactory>();
            serviceCollection.TryAddSingleton<IAuthTokenProvider, AuthTokenProvider>();
        }

        /// <summary>
        /// Registers services to control profile sessions.
        /// </summary>
        private static void RegisterTraceControls(IServiceCollection serviceCollection)
        {
            // Dependency
            serviceCollection.TryAddSingleton<IThreadUtilities>(p => ThreadUtilities.Instance.Value);
            // Trace control
            serviceCollection.TryAddSingleton<ITraceControl, DiagnosticsClientTraceControl>();
        }

        private void RegisterFrontendClient(IServiceCollection serviceCollection)
            => serviceCollection.AddSingleton(
                p => ActivatorUtilities.CreateInstance<ProfilerFrontendClientFactory>(p).CreateProfilerFrontendClient()
            );
    }
}
