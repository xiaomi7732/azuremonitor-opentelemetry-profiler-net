using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Azure.Monitor.OpenTelemetry.Profiler.Core.EventListeners;

internal class TraceSessionListenerFactory
{
    // Internal, undocumented kill switch. Lets us A/B the two request-event sources during the
    // migration away from OpenTelemetry-Sdk's RequestStart/Stop toward DiagnosticSource.
    // Accepted values (case-insensitive): "ds" | "otel" | "both". Anything else falls back to default.
    internal const string RequestSourceEnvVarName = "MICROSOFT_PROFILER_INTERNAL_REQUEST_SOURCE";

    private const RequestSourceMode DefaultRequestSourceMode = RequestSourceMode.DiagnosticSource;

    private readonly IServiceProvider _serviceProvider;

    public TraceSessionListenerFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    public TraceSessionListener Create()
    {
        ILogger<TraceSessionListenerFactory> logger = _serviceProvider
            .GetRequiredService<ILogger<TraceSessionListenerFactory>>();

        RequestSourceMode mode = ReadRequestSourceMode(logger);
        logger.LogInformation("Request event source mode: {mode}", mode);

        // One RequestActivityRelay per listener lifetime, shared across this listener's handlers so that
        // a Start emitted by one source can correlate with a Stop emitted by another (when in Both mode).
        RequestActivityRelay relay = ActivatorUtilities.CreateInstance<RequestActivityRelay>(_serviceProvider);

        List<IEventSourceHandler> handlers = new(capacity: 3);

        if (mode is RequestSourceMode.OpenTelemetrySdk or RequestSourceMode.Both)
        {
            handlers.Add(ActivatorUtilities.CreateInstance<OpenTelemetrySdkEventSourceHandler>(_serviceProvider, relay));
        }

        if (mode is RequestSourceMode.DiagnosticSource or RequestSourceMode.Both)
        {
            handlers.Add(ActivatorUtilities.CreateInstance<DiagnosticSourceEventSourceHandler>(_serviceProvider, relay));
        }

        // TPL is unrelated to the request-source choice; it just propagates activity IDs.
        handlers.Add(ActivatorUtilities.CreateInstance<TplEventSourceHandler>(_serviceProvider));

        return ActivatorUtilities.CreateInstance<TraceSessionListener>(_serviceProvider, (IEnumerable<IEventSourceHandler>)handlers);
    }

    private static RequestSourceMode ReadRequestSourceMode(ILogger logger)
    {
        string? raw = Environment.GetEnvironmentVariable(RequestSourceEnvVarName);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return DefaultRequestSourceMode;
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
            raw, RequestSourceEnvVarName, DefaultRequestSourceMode);
        return DefaultRequestSourceMode;
    }
}
