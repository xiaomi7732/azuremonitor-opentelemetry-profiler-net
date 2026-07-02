// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Azure.Monitor.OpenTelemetry.Profiler.HostingStartup;

/// <summary>
/// Detects which telemetry stack the host application is built against so the codeless HostingStartup
/// can route to the matching profiler.
/// </summary>
internal interface ITelemetryStackDetector
{
    /// <summary>Detects the application's telemetry stack.</summary>
    TelemetryStack Detect();
}
