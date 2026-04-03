// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Monitor.OpenTelemetry.Profiler.Core;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;

namespace Azure.Monitor.OpenTelemetry.Profiler;

public static class OpenTelemetryBuilderExtensions
{
    /// <summary>
    /// Register the services needed to enable Profiler.
    /// </summary>
    /// <param name="builder">The service container.</param>
    /// <param name="configureServiceProfiler">An action to customize the behavior of the profiler.</param>
    public static IOpenTelemetryBuilder AddAzureMonitorProfiler(this IOpenTelemetryBuilder builder, Action<ServiceProfilerOptions>? configureServiceProfiler = null)
    {
        builder.Services.AddAzureMonitorProfiler(configureServiceProfiler);
        return builder;
    }
}


