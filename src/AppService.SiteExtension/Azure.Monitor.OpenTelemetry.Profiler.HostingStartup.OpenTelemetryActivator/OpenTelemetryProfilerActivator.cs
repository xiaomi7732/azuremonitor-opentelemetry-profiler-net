// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.DependencyInjection;

namespace Azure.Monitor.OpenTelemetry.Profiler.HostingStartup.OpenTelemetryActivator;

/// <summary>
/// Per-stack activator for OpenTelemetry-based applications. Loaded reflectively by the HostingStartup
/// router (see <c>ReflectionProfilerActivatorInvoker</c>) from the <c>otel\</c> payload subfolder, so the
/// OpenTelemetry profiler closure this project brings stays isolated from the classic stack's closure.
///
/// The <see cref="Enable"/> signature (public static, single <see cref="IServiceCollection"/> parameter) is
/// the contract the router invokes by name; keep it in sync with the classic activator.
/// </summary>
internal static class OpenTelemetryProfilerActivator
{
    /// <summary>Registers the Azure Monitor OpenTelemetry profiler into <paramref name="services"/>.</summary>
    internal static void Enable(IServiceCollection services) => services.AddAzureMonitorProfiler();
}
