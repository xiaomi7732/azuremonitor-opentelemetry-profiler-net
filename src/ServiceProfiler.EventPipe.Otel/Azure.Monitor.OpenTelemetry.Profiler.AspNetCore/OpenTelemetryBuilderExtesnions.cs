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
    public static IOpenTelemetryBuilder UseServiceProfiler(this OpenTelemetryBuilder builder, Action<ServiceProfilerOptions>? configureServiceProfiler)
      => UseServiceProfiler((IOpenTelemetryBuilder)builder, configureServiceProfiler);

    public static IOpenTelemetryBuilder UseServiceProfiler(this IOpenTelemetryBuilder builder, Action<ServiceProfilerOptions>? configureServiceProfiler)
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
            ServiceProfilerOptions profilerOptions = p.GetRequiredService<IOptions<ServiceProfilerOptions>>().Value;
            return Options.Create(profilerOptions);
        });

        builder.Services.AddServiceProfilerCore();

        builder.Services.AddSingleton<IServiceProfilerAgentBootstrap, ServiceProfilerAgentBootstrap>();
        builder.Services.AddHostedService<ProfilerBackgroundService>();
        return builder;
    }
}


