using Microsoft.Extensions.Logging;
using System.Diagnostics.Tracing;

namespace Azure.Monitor.OpenTelemetry.Profiler.Core.EventListeners;

/// <summary>
/// Handler for the <c>OpenTelemetry-Sdk</c> EventSource. Maps its RequestStart (24) /
/// RequestStop (25) events onto the shared <see cref="RequestActivityRelay"/>.
/// </summary>
internal sealed class OpenTelemetrySdkEventSourceHandler : IEventSourceHandler
{
    public const string EventSourceName = "OpenTelemetry-Sdk";

    // Event ids defined by the OpenTelemetry-Sdk EventSource.
    private const int RequestStartEventId = 24;
    private const int RequestStopEventId = 25;

    private readonly RequestActivityRelay _relay;
    private readonly ILogger<OpenTelemetrySdkEventSourceHandler> _logger;

    public OpenTelemetrySdkEventSourceHandler(
        RequestActivityRelay relay,
        ILogger<OpenTelemetrySdkEventSourceHandler> logger)
    {
        _relay = relay ?? throw new ArgumentNullException(nameof(relay));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool CanHandle(EventSource eventSource)
        => string.Equals(eventSource.Name, EventSourceName, StringComparison.OrdinalIgnoreCase);

    public void Enable(EventListener listener, EventSource eventSource)
    {
        // 0x0000F covers the keyword bits the OTel SDK uses for activity start/stop reporting.
        const EventKeywords keywords = (EventKeywords)0x0000F;
        _logger.LogDebug("Enabling EventSource: {eventSourceName}", eventSource.Name);
        listener.EnableEvents(eventSource, EventLevel.Verbose, keywords);
    }

    public void OnEventWritten(EventWrittenEventArgs eventData)
    {
        if (eventData.EventId != RequestStartEventId && eventData.EventId != RequestStopEventId)
        {
            return;
        }

        string requestName = eventData.GetPayload<string>("name") ?? "Unknown";
        string id = eventData.GetPayload<string>("id") ?? throw new InvalidDataException("id payload is missing.");

        (string requestId, string operationId) = RequestActivityRelay.ExtractKeyIds(id);

        if (eventData.EventId == RequestStartEventId)
        {
            _relay.HandleRequestStart(eventData, requestName, requestId, operationId, id);
        }
        else
        {
            _relay.HandleRequestStop(eventData, requestName, requestId, operationId, id);
        }
    }
}
