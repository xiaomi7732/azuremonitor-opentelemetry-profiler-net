namespace Azure.Monitor.OpenTelemetry.Profiler.Core.EventListeners;

/// <summary>
/// Selects which EventSource(s) the <see cref="TraceSessionListener"/> consumes for ASP.NET Core
/// HTTP-in request start/stop events. Internal kill switch — not user-facing configuration.
/// </summary>
internal enum RequestSourceMode
{
    /// <summary>
    /// Only the <c>Microsoft-Diagnostics-DiagnosticSource</c> bridge is used. Default.
    /// </summary>
    DiagnosticSource,

    /// <summary>
    /// Only the <c>OpenTelemetry-Sdk</c> RequestStart/Stop events are used. Legacy.
    /// </summary>
    OpenTelemetrySdk,

    /// <summary>
    /// Both sources are subscribed. The shared <see cref="RequestActivityRelay"/> dedupes
    /// by activity id so the relay EventSource still sees exactly one Start/Stop pair.
    /// </summary>
    Both,
}
