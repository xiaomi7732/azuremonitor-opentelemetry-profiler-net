using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Azure.Monitor.OpenTelemetry.Profiler.Core.EventListeners;

internal class TraceSessionListenerFactory
{
    private readonly IServiceProvider _serviceProvider;

    public TraceSessionListenerFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    public TraceSessionListener Create()
    {
        ILogger<TraceSessionListenerFactory> logger = _serviceProvider
            .GetRequiredService<ILogger<TraceSessionListenerFactory>>();

        RequestSourceMode mode = RequestSourceModeResolver.Resolve(logger);
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
}
