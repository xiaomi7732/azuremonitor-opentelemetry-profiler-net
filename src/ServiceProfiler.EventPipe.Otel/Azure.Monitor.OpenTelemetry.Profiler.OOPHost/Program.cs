// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Monitor.OpenTelemetry.AspNetCore;
using Azure.Monitor.OpenTelemetry.Profiler.Core;
using Azure.Monitor.OpenTelemetry.Profiler.OOPHost;
using Microsoft.ApplicationInsights.Profiler.Shared.Contracts;
using Microsoft.ApplicationInsights.Profiler.Shared.Services;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

IServiceCollection services = builder.Services;
services.AddLogging(opt =>
{
    opt.AddSimpleConsole(console =>
    {
        console.SingleLine = true;
    });
});
services.AddOptions();
services.AddOpenTelemetry().UseAzureMonitor();

services.AddOptions<ServiceProfilerOOPHostOptions>().Configure<IConfiguration, IOptions<AzureMonitorOptions>>((opt, configuration, azureMonitorOptions) =>
{
    configuration.GetSection("ServiceProfiler").Bind(opt);

    AzureMonitorOptions? monitorOptions = azureMonitorOptions.Value;

    string? azureMonitorConnectionString = monitorOptions.ConnectionString;
    if (string.IsNullOrEmpty(opt.ConnectionString))
    {
        opt.ConnectionString = azureMonitorConnectionString;
    }

    if (opt.Credential is null)
    {
        opt.Credential = monitorOptions.Credential;
        // Notice: the credential could still be null because monitorOptions is nullable, and its Credential object could be null.
    }
});

services.AddSingleton<IOptions<ServiceProfilerOptions>>(p =>
{
    ServiceProfilerOOPHostOptions profilerOptions = GetRequiredOptions<ServiceProfilerOOPHostOptions>(p);
    return Options.Create(profilerOptions);
});

services.AddSingleton<IOptions<UserConfigurationBase>>(p =>
{
    ServiceProfilerOOPHostOptions profilerOptions = GetRequiredOptions<ServiceProfilerOOPHostOptions>(p);
    return Options.Create(profilerOptions);
});

services.AddServiceProfilerCore();

// Overwrite
OverwriteServices(services);

services.AddSingleton<IServiceProfilerAgentBootstrap>(p =>
{
    ServiceProfilerOptions userConfiguration = GetRequiredOptions<ServiceProfilerOptions>(p);
    // Choose one by configurations to register.
    return userConfiguration.IsDisabled ?
        ActivatorUtilities.CreateInstance<DisabledAgentBootstrap>(p) :
        ActivatorUtilities.CreateInstance<ServiceProfilerAgentBootstrap>(p);
});

services.AddHostedService<ProfilerBackgroundService>();



var host = builder.Build();
host.Run();

static T GetRequiredOptions<T>(IServiceProvider p)
    where T : class
{
    return p.GetRequiredService<IOptions<T>>().Value ?? throw new InvalidOperationException($"Option {typeof(T).FullName} is required.");
}

static void OverwriteServices(IServiceCollection services)
{
    // Overwrite ITargetProcess by using TargetProcessService for out of proc implementation.
    services.AddSingleton<ITargetProcess, TargetProcessService>();

    // Overwrite IServiceProfilerProvider
    services.AddSingleton<IServiceProfilerProvider, OOPProfilerProvider>();

    services.AddSingleton<IPostStopProcessorFactory, OOPPostStopProcessorFactory>();
}