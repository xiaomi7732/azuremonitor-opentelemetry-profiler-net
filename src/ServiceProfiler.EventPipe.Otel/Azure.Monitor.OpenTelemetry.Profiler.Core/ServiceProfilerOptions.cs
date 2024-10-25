using Microsoft.ApplicationInsights.Profiler.Shared.Contracts;

namespace Azure.Monitor.OpenTelemetry.Profiler.Core;

/// <summary>
/// Service Profiler Options
/// </summary>
public class ServiceProfilerOptions : UserConfigurationBase
{
    /// <summary>
    /// Gets or sets the connection string.
    /// </summary>
    public string? ConnectionString { get; set; } = string.Empty;
}