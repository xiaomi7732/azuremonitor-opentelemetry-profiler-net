using Microsoft.Extensions.Logging;
using System.Diagnostics.Tracing;

namespace Azure.Monitor.OpenTelemetry.Profiler.Core.EventListeners;

/// <summary>
/// Handler for the <c>Microsoft-Diagnostics-DiagnosticSource</c> EventSource bridge.
/// Subscribes to ASP.NET Core HTTP-in start/stop diagnostic events via <c>FilterAndPayloadSpecs</c>,
/// mapping them onto the bridge's <c>Activity1Start</c>/<c>Activity1Stop</c> EventSource events,
/// then forwards parsed payloads to the shared <see cref="RequestActivityRelay"/>.
/// </summary>
internal sealed class DiagnosticSourceEventSourceHandler : IEventSourceHandler
{
    public const string EventSourceName = "Microsoft-Diagnostics-DiagnosticSource";

    // FilterAndPayloadSpecs grammar: see DiagnosticSourceEventSource source-of-truth comment block.
    //   <DiagnosticSourceName>/<DiagnosticEventName>@<EventSourceEventName>:<TRANSFORM_SPECS>
    // The leading '-' suppresses implicit payload serialization; we explicitly project only Activity name + Id.
    private const string FilterAndPayloadSpecs =
        "Microsoft.AspNetCore/Microsoft.AspNetCore.Hosting.HttpRequestIn.Start@Activity1Start:" +
            "-ActivityName=Activity.OperationName" +
            ";Id=Activity.Id" +
        "\n" +
        "Microsoft.AspNetCore/Microsoft.AspNetCore.Hosting.HttpRequestIn.Stop@Activity1Stop:" +
            "-ActivityName=Activity.OperationName" +
            ";Id=Activity.Id";

    private readonly RequestActivityRelay _relay;
    private readonly ILogger<DiagnosticSourceEventSourceHandler> _logger;

    public DiagnosticSourceEventSourceHandler(
        RequestActivityRelay relay,
        ILogger<DiagnosticSourceEventSourceHandler> logger)
    {
        _relay = relay ?? throw new ArgumentNullException(nameof(relay));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool CanHandle(EventSource eventSource)
        => string.Equals(eventSource.Name, EventSourceName, StringComparison.Ordinal);

    public void Enable(EventListener listener, EventSource eventSource)
    {
        _logger.LogDebug("Enabling EventSource: {eventSourceName}", eventSource.Name);

        // Keyword 0x2 == DiagnosticSourceEventSource.Keywords.Events, which gates FilterAndPayloadSpecs processing.
        // Do NOT set 0x800 (IgnoreShortCutKeywords): keeping it off preserves the AspNetCoreHosting/EFCore shortcut keywords.
        listener.EnableEvents(
            eventSource,
            EventLevel.Informational,
            (EventKeywords)0x2,
            new Dictionary<string, string>
            {
                ["FilterAndPayloadSpecs"] = FilterAndPayloadSpecs,
            });
    }

    public void OnEventWritten(EventWrittenEventArgs eventData)
    {
        // We only care about the bridged Activity{N}Start / Activity{N}Stop events.
        string? eventName = eventData.EventName;
        if (eventName is null || (!eventName.EndsWith("Start", StringComparison.Ordinal) && !eventName.EndsWith("Stop", StringComparison.Ordinal)))
        {
            return;
        }

        // The bridged Activity1Start/Activity1Stop events have the payload shape:
        //   (string SourceName, string EventName, IEnumerable<KeyValuePair<string,string>> Arguments)
        // EventListener surfaces Arguments as object[] of IDictionary<string,object> with "Key"/"Value".
        object[] arguments = eventData.GetPayload<object[]>("Arguments")
            ?? throw new InvalidDataException("Arguments payload is missing.");

        string requestName = GetArgumentValue(arguments, "ActivityName") ?? "Unknown";
        string id = GetArgumentValue(arguments, "Id") ?? throw new InvalidDataException("Id argument is missing.");
        (string operationId, string requestId) = RequestActivityRelay.ExtractKeyIds(id);

        if (eventName.EndsWith("Start", StringComparison.Ordinal))
        {
            _relay.HandleRequestStart(eventData, requestName, requestId, operationId, id);
        }
        else
        {
            _relay.HandleRequestStop(eventData, requestName, requestId, operationId, id);
        }
    }

    // Each element of the bridged Arguments payload is an IDictionary<string,object>
    // with two entries: { "Key" -> <name>, "Value" -> <value> }.
    private static string? GetArgumentValue(object[] arguments, string key)
    {
        foreach (object? argument in arguments)
        {
            if (argument is IDictionary<string, object> kvp
                && kvp.TryGetValue("Key", out object? k)
                && k is string keyName
                && string.Equals(keyName, key, StringComparison.Ordinal)
                && kvp.TryGetValue("Value", out object? v))
            {
                return v as string;
            }
        }

        return null;
    }
}
