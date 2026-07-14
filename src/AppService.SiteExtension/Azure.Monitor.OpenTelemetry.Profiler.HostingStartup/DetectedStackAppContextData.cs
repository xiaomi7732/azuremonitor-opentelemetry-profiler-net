// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Azure.Monitor.OpenTelemetry.Profiler.HostingStartup;

/// <summary>
/// Shared contract between the <c>StartupHook</c> (which detects the telemetry stack and scopes the
/// assembly resolver to the matching payload subfolder) and the HostingStartup router (which activates the
/// matching profiler). This file is compiled into both assemblies (linked source), so the two stay in sync.
///
/// The StartupHook stores the detected <see cref="TelemetryStack"/> name under <see cref="Key"/> via
/// <c>AppContext</c>; the router reads it back. A string is used - not the enum - because the two assemblies
/// compile independent copies of the enum type, which therefore have different identities across the
/// assembly boundary.
/// </summary>
internal static class DetectedStackAppContextData
{
    /// <summary>AppContext data key carrying the detected stack name (an <see cref="TelemetryStack"/> value).</summary>
    public const string Key = "Azure.Monitor.OpenTelemetry.Profiler.Codeless.DetectedStack";

    /// <summary>
    /// Maps a detected stack to the payload subfolder that holds its self-contained profiler closure. Each
    /// stack lives in its own folder so their dependencies are never unified into a single shared version.
    /// Returns an empty string when no stack-specific folder applies (<see cref="TelemetryStack.None"/>).
    /// </summary>
    public static string ToPayloadSubfolder(TelemetryStack stack) => stack switch
    {
        TelemetryStack.OpenTelemetry => "otel",
        TelemetryStack.LegacyApplicationInsights => "classic",
        _ => string.Empty,
    };
}
