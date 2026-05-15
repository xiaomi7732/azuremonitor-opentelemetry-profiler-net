using Microsoft.Extensions.Logging;

namespace Azure.Monitor.OpenTelemetry.Profiler.Core.EventListeners;

/// <summary>
/// Resolves the request-event-source <see cref="RequestSourceMode"/> from the internal kill-switch
/// environment variable. Shared by <see cref="TraceSessionListenerFactory"/> (in-process handler
/// selection) and the EventPipe provider configuration (which providers to enable on the session).
/// </summary>
internal static class RequestSourceModeResolver
{
    // Internal, undocumented kill switch. Lets us A/B the two request-event sources during the
    // migration away from OpenTelemetry-Sdk's RequestStart/Stop toward DiagnosticSource.
    // Accepted values (case-insensitive): "ds" | "otel" | "both". Anything else falls back to default.
    internal const string EnvVarName = "MICROSOFT_PROFILER_INTERNAL_REQUEST_SOURCE";

    internal const RequestSourceMode Default = RequestSourceMode.DiagnosticSource;

    public static RequestSourceMode Resolve(ILogger logger)
    {
        string? raw = Environment.GetEnvironmentVariable(EnvVarName);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Default;
        }

        return raw.Trim().ToLowerInvariant() switch
        {
            "ds" => RequestSourceMode.DiagnosticSource,
            "otel" => RequestSourceMode.OpenTelemetrySdk,
            "both" => RequestSourceMode.Both,
            _ => LogUnrecognizedAndUseDefault(logger, raw),
        };
    }

    private static RequestSourceMode LogUnrecognizedAndUseDefault(ILogger logger, string raw)
    {
        logger.LogWarning(
            "Unrecognized value '{value}' for {envVar}; falling back to {default}. Expected: ds | otel | both.",
            raw, EnvVarName, Default);
        return Default;
    }
}
