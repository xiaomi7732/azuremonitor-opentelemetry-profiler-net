// -----------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
// -----------------------------------------------------------------------------

using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services;

/// <summary>
/// Maps a <see cref="ConnectionStringValidationResult"/> to an actionable diagnostic message.
/// Shared by the early startup pre-check (<c>ProfilerBackgroundService</c>) and the profiler
/// bootstrap so the wording stays consistent and defined in one place.
/// </summary>
internal static class ConnectionStringDiagnostics
{
    /// <summary>Suffix appended to an error to state the profiler will not start.</summary>
    public const string ProfilerWontStartSuffix = " Application Insights Profiler won't start.";

    /// <summary>
    /// A hint that the Azure Monitor exporter itself requires a connection string. Without one the
    /// exporter throws while building its telemetry providers and faults application startup - a
    /// failure that originates in the exporter, not the profiler.
    /// </summary>
    public const string ExporterConnectionStringHint =
        " Note: the Azure Monitor exporter also requires a valid connection string; if none is configured " +
        "(via APPLICATIONINSIGHTS_CONNECTION_STRING or UseAzureMonitor(...)) it will fail application startup.";

    private const string NoConnectionStringMessage = "No connection string is set.";
    private const string InstrumentationKeyMessage = "Instrumentation key is not set or malformed in the connection string.";

    /// <summary>
    /// Returns an actionable error message describing why the connection string is unusable, or
    /// <see langword="null"/> when it is valid.
    /// </summary>
    public static string? GetConfigurationError(ConnectionStringValidationResult validation) => validation switch
    {
        ConnectionStringValidationResult.Valid => null,
        ConnectionStringValidationResult.NotConfigured => NoConnectionStringMessage,
        ConnectionStringValidationResult.Empty => "The connection string is empty.",
        ConnectionStringValidationResult.InvalidInstrumentationKey => InstrumentationKeyMessage,
        ConnectionStringValidationResult.Malformed => "The connection string is malformed and could not be parsed. Verify the connection string and that it contains a valid instrumentation key.",
        _ => "The connection string is invalid.",
    };
}
