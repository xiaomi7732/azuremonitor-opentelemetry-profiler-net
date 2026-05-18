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
    //
    // We use the ActivitySource path (the "[AS]" prefix), NOT the DiagnosticListener path:
    //   [AS]<ActivitySourceName>/[<ActivityName>][:<TRANSFORM_SPECS>]
    //
    // Why ActivitySource and not DiagnosticListener:
    //   The ActivitySource bridge is Activity-aware — it auto-populates standard Activity fields
    //   (Id, ActivityName, parent ids, status, etc.) into the Activity{N}Start/Stop event's
    //   Arguments payload, with no transform spec required. The DiagnosticListener path can only
    //   walk properties of the diagnostic event payload object (e.g. HttpContext for
    //   HttpRequestIn.Start), which has no Activity property — so any "Activity.Id" transform
    //   resolves to null and is silently dropped (verified empirically).
    //
    // Why EventSource bridge and not direct DiagnosticListener subscription:
    //   We need to keep the option open to consume these events out-of-process via EventPipe / ETW
    //   in the future. Direct DiagnosticListener subscription is in-process only (no IPC).
    //   The EventSource bridge re-emits the same events as ETW/EventPipe events, so the same spec
    //   works whether the consumer is an in-proc EventListener (today) or an out-of-proc
    //   DiagnosticsClient session (tomorrow).
    //
    // **REQUIRES .NET 8+** because ASP.NET Core only exposed an ActivitySource named
    // "Microsoft.AspNetCore" starting in .NET 8. On .NET 6/7 ASP.NET Core created
    // request Activities directly via `new Activity(...)`/`DiagnosticSource.StartActivity(...)`
    // without an ActivitySource, so this spec captures nothing on those runtimes. Older
    // runtimes are covered by the OpenTelemetry-Sdk handler (OTel's ASP.NET Core
    // instrumentation has the same .NET-8+ floor in modern releases anyway).
    //
    // The ActivitySource name is "Microsoft.AspNetCore" (NOT "Microsoft.AspNetCore.Hosting" —
    // that's the ActivityName / OperationName, e.g. "Microsoft.AspNetCore.Hosting.HttpRequestIn").
    internal const string FilterAndPayloadSpecs = "[AS]Microsoft.AspNetCore/";

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
        // We only care about the bridged ActivityStart / ActivityStop events (Events 11/12 from the
        // ActivitySource bridge path). Their payload shape is:
        //   (string SourceName, string ActivityName, IEnumerable<KeyValuePair<string,string>> Arguments)
        // SourceName + ActivityName are top-level payload fields. Activity Id / parent ids / status
        // are projected into Arguments by the runtime bridge.
        string? eventName = eventData.EventName;
        if (eventName is null || (!eventName.EndsWith("Start", StringComparison.Ordinal) && !eventName.EndsWith("Stop", StringComparison.Ordinal)))
        {
            return;
        }

        string requestName = eventData.GetPayload<string>("ActivityName") ?? "Unknown";

        // Arguments is an object[] of IDictionary<string,object> with "Key"/"Value" entries.
        object[]? arguments = eventData.GetPayload<object[]>("Arguments");
        string? id = arguments is null ? null : GetArgumentValue(arguments, "Id");
        if (string.IsNullOrEmpty(id))
        {
            // Foreign listeners can route different diagnostic events into the shared
            // Activity{N}Start/Stop slots with different payload projections. Skip silently.
            return;
        }

        (string requestId, string operationId) = RequestActivityRelay.ExtractKeyIds(id);

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
