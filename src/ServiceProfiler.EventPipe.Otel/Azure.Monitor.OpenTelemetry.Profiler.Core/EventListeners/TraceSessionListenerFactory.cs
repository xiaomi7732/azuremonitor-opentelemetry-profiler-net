using Microsoft.Extensions.DependencyInjection;

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
        // One RequestActivityRelay per listener lifetime, shared across this listener's handlers so that
        // a Start emitted by one source can correlate with a Stop emitted by another.
        RequestActivityRelay relay = ActivatorUtilities.CreateInstance<RequestActivityRelay>(_serviceProvider);

        IEventSourceHandler[] handlers =
        [
            ActivatorUtilities.CreateInstance<OpenTelemetrySdkEventSourceHandler>(_serviceProvider, relay),
            ActivatorUtilities.CreateInstance<DiagnosticSourceEventSourceHandler>(_serviceProvider, relay),
            ActivatorUtilities.CreateInstance<TplEventSourceHandler>(_serviceProvider),
        ];

        return ActivatorUtilities.CreateInstance<TraceSessionListener>(_serviceProvider, (IEnumerable<IEventSourceHandler>)handlers);
    }
}
