using Microsoft.Extensions.Logging;
using System.Diagnostics.Tracing;

namespace Azure.Monitor.OpenTelemetry.Profiler.Core.EventListeners;

/// <summary>
/// Handler for the TPL EventSource. Activity IDs aren't enabled by default; turning on keyword 0x80
/// makes them flow so that other listeners (and ETW consumers) see correlated activity IDs.
/// This handler does not consume any events itself.
/// </summary>
internal sealed class TplEventSourceHandler : IEventSourceHandler
{
    public const string EventSourceName = "System.Threading.Tasks.TplEventSource";

    private readonly ILogger<TplEventSourceHandler> _logger;

    public TplEventSourceHandler(ILogger<TplEventSourceHandler> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool CanHandle(EventSource eventSource)
        => string.Equals(eventSource.Name, EventSourceName, StringComparison.Ordinal);

    public void Enable(EventListener listener, EventSource eventSource)
    {
        _logger.LogDebug("Enabling EventSource: {eventSourceName}", eventSource.Name);
        // Keyword 0x80 turns on Activity IDs.
        listener.EnableEvents(eventSource, EventLevel.LogAlways, (EventKeywords)0x80);
    }

    public void OnEventWritten(EventWrittenEventArgs eventData)
    {
        // No-op: we enable this source only so that activity IDs propagate.
    }
}
