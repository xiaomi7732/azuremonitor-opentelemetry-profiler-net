using Azure.Monitor.OpenTelemetry.AspNetCore;
using Azure.Monitor.OpenTelemetry.Profiler.Core;
using Microsoft.ApplicationInsights.Profiler.Shared.Contracts;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenTelemetry;

namespace Azure.Monitor.OpenTelemetry.Profiler.AspNetCore;

public static class OpenTelemetryBuilderExtesnions
{
    [Obsolete($"Use {nameof(UseProfiler)} instead", error: true)]
    public static IOpenTelemetryBuilder UseServiceProfiler(this IOpenTelemetryBuilder builder, Action<ServiceProfilerOptions>? configureServiceProfiler = null)
        => UseProfiler(builder, configureServiceProfiler);

    /// <summary>
    /// Register the services needed to enable Profiler.
    /// </summary>
    /// <param name="builder">The service container.</param>
    /// <param name="configureServiceProfiler">An action to customize the behavior of the profiler.</param>
    public static IOpenTelemetryBuilder UseProfiler(this IOpenTelemetryBuilder builder, Action<ServiceProfilerOptions>? configureServiceProfiler = null)
    {
        builder.Services.AddLogging();
        builder.Services.AddOptions();

        builder.Services.AddOptions<ServiceProfilerOptions>().Configure<IConfiguration, IOptions<AzureMonitorOptions>>((opt, configuration, azureMonitorOptions) =>
        {
            configuration.GetSection("ServiceProfiler").Bind(opt);
            configureServiceProfiler?.Invoke(opt);

            string? azureMnoitorConnectionStirng = azureMonitorOptions?.Value?.ConnectionString;
            if (string.IsNullOrEmpty(opt.ConnectionString))
            {
                opt.ConnectionString = azureMnoitorConnectionStirng;
            }
        });

        builder.Services.AddSingleton<IOptions<UserConfigurationBase>>(p =>
        {
            ServiceProfilerOptions profilerOptions = GetRequiredOptions<ServiceProfilerOptions>(p);
            return Options.Create(profilerOptions);
        });

        builder.Services.AddServiceProfilerCore();

        builder.Services.AddSingleton<IServiceProfilerAgentBootstrap>(p =>
        {
            ServiceProfilerOptions userConfiguration = GetRequiredOptions<ServiceProfilerOptions>(p);
            // Choose one by configurations to register.
            return userConfiguration.IsDisabled ?
                ActivatorUtilities.CreateInstance<DisabledAgentBootstrap>(p) :
                ActivatorUtilities.CreateInstance<ServiceProfilerAgentBootstrap>(p);
        });

        builder.Services.AddHostedService<ProfilerBackgroundService>();
        return builder;
    }

    private static T GetRequiredOptions<T>(IServiceProvider p)
        where T : class
    {
        return p.GetRequiredService<IOptions<T>>().Value ?? throw new InvalidOperationException($"Option {typeof(T).FullName} is required.");
    }
}


