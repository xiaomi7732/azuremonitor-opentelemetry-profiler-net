// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

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

services.AddOptions<ServiceProfilerOOPHostOptions>().Configure<IConfiguration, ILogger<Program>>((opt, configuration, logger) =>
{

    PrintSections(configuration.GetChildren(), logger);
    configuration.GetSection("ServiceProfiler").Bind(opt);

    if (string.IsNullOrEmpty(opt.ConnectionString))
    {
        const string connectionStringKey = "APPLICATIONINSIGHTS_CONNECTION_STRING";
        string? directConnectionStringEnv = configuration.GetValue<string>(connectionStringKey);
        if (!string.IsNullOrEmpty(directConnectionStringEnv))
        {
            logger.LogInformation("Set connection string by environment variable of {name}", connectionStringKey);
            opt.ConnectionString = directConnectionStringEnv;
        }
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

static void PrintSections(IEnumerable<IConfigurationSection> sections, ILogger logger)
{
    foreach (IConfigurationSection line in sections)
    {
        logger.LogTrace("{path} == {value}", line.Path, line.Value);

        IEnumerable<IConfigurationSection> children = line.GetChildren();
        if (children is not null && children.Any())
        {
            PrintSections(children, logger);
        }
    }
}