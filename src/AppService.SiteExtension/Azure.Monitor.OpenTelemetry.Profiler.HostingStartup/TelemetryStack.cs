// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Azure.Monitor.OpenTelemetry.Profiler.HostingStartup;

/// <summary>
/// The telemetry stack an application is built against, used to decide whether/how to enable the profiler.
/// </summary>
internal enum TelemetryStack
{
    /// <summary>No supported telemetry stack detected.</summary>
    None,

    /// <summary>
    /// A supported OpenTelemetry-based stack: the Azure Monitor OpenTelemetry distro, the current
    /// OpenTelemetry-based Application Insights SDK (3.x), or a manual OpenTelemetry setup. Enabled via
    /// the Azure Monitor OpenTelemetry profiler.
    /// </summary>
    OpenTelemetry,

    /// <summary>
    /// The legacy classic Application Insights SDK (2.x). This profiler does not support it; use the
    /// Application Insights Profiler for ASP.NET Core instead.
    /// </summary>
    LegacyApplicationInsights,
}
