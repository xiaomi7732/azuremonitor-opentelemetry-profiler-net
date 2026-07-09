// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.DependencyInjection;

namespace Azure.Monitor.OpenTelemetry.Profiler.HostingStartup.ClassicActivator;

/// <summary>
/// Per-stack activator for legacy classic Application Insights (2.x) applications. Loaded reflectively by
/// the HostingStartup router (see <c>ReflectionProfilerActivatorInvoker</c>) from the <c>classic\</c> payload
/// subfolder, so the classic profiler closure this project brings stays isolated from the OpenTelemetry
/// stack's closure.
///
/// Mirrors the classic HostingStartup (HostingStartup30): ensure the Application Insights telemetry pipeline
/// the profiler depends on is present, then add the profiler. Both calls are idempotent.
///
/// The <see cref="Enable"/> signature (public static, single <see cref="IServiceCollection"/> parameter) is
/// the contract the router invokes by name; keep it in sync with the OpenTelemetry activator.
/// </summary>
public static class ClassicProfilerActivator
{
    /// <summary>Registers the classic Application Insights profiler into <paramref name="services"/>.</summary>
    public static void Enable(IServiceCollection services)
    {
        services.AddApplicationInsightsTelemetry();
        services.AddServiceProfiler();
    }
}
