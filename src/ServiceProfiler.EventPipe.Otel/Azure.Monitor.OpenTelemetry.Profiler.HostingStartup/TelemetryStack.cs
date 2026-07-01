// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Azure.Monitor.OpenTelemetry.Profiler.HostingStartup;

/// <summary>
/// The telemetry stack an application is built against, used to decide which profiler (if any) to enable.
/// </summary>
internal enum TelemetryStack
{
    /// <summary>No supported telemetry stack detected.</summary>
    None,

    /// <summary>The app uses OpenTelemetry (including the Azure Monitor OpenTelemetry distro).</summary>
    OpenTelemetry,

    /// <summary>The app uses the classic Application Insights SDK.</summary>
    ApplicationInsights,

    /// <summary>Both stacks detected - ambiguous, so no profiler is enabled.</summary>
    Both,
}
