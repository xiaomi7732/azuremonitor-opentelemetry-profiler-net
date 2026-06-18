using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics.Tracing;
using System.Threading;

namespace Azure.Monitor.OpenTelemetry.Profiler.Core.EventListeners;

/// <summary>
/// Tracks in-flight "interesting" request activities (ASP.NET Core HTTP-in, Azure Service Bus
/// processor messages, receiver-based message consumption, and Azure Functions isolated worker
/// invocations) and forwards matched start/stop pairs onto
/// <see cref="AzureMonitorOpenTelemetryProfilerDataAdapterEventSource"/>.
///
/// Lives once per <see cref="TraceSessionListener"/> and is shared by every <see cref="IEventSourceHandler"/>
/// that produces request activities, so a Start emitted by one source can correlate with (or be deduped
/// against) a Stop emitted by another.
/// </summary>
internal sealed class RequestActivityRelay
{
    private const string AspNetCoreHttpRequestInName = "Microsoft.AspNetCore.Hosting.HttpRequestIn";
    private const string ServiceBusProcessMessageName = "ServiceBusProcessor.ProcessMessage";
    private const string ServiceBusProcessSessionMessageName = "ServiceBusSessionProcessor.ProcessSessionMessage";
    // Azure Functions isolated worker per-invocation activity (ActivitySource
    // "Microsoft.Azure.Functions.Worker"). In the isolated model the Service Bus SDK runs in the host
    // process, so this worker-side invocation span is the only request-like activity visible to an
    // in-worker profiler. Note: this fires for every trigger type (HTTP, timer, Service Bus, ...), not
    // just Service Bus.
    //
    // The activity's operation name depends on the worker's OpenTelemetry schema version
    // (Microsoft.Azure.Functions.Worker, TelemetryProvider.GetActivityName):
    //   - schema <= 1.17.0: the literal "Invoke" (TraceConstants.ActivityAttributes.InvokeActivityName).
    //   - schema >= 1.37.0: "function <FunctionName>", e.g. "function MySBFuncTest"
    //     ($"{FunctionActivityName} {FunctionDefinition.Name}", FunctionActivityName == "function").
    // We match both so the capture is robust across worker versions. The "function " prefix is safe here
    // because gate 1 (FilterAndPayloadSpecs) only subscribes to this worker source plus ASP.NET Core and
    // the Service Bus processor sources, none of which produce "function "-prefixed activity names.
    private const string FunctionsWorkerInvokeName = "Invoke";
    // Shared with DiagnosticSourceEventSourceHandler, which gates the parent-id correlation remap on this
    // same prefix (the schema 1.37.0+ "function <name>" activity is ActivityKind.Internal / a dependency,
    // whereas the "Invoke" activity is ActivityKind.Server / a request).
    internal const string FunctionsWorkerActivityNamePrefix = "function ";

    private readonly ILogger<RequestActivityRelay> _logger;
    private readonly ConcurrentDictionary<string, byte> _startedActivityIds = new();
    private int _hasActivityReported;

    public RequestActivityRelay(ILogger<RequestActivityRelay> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// We capture HTTP-in requests, Service Bus processor messages, and Azure Functions isolated worker
    /// invocations — i.e. activities that Azure Monitor records as *requests* (ActivityKind.Server /
    /// Consumer), or the worker invocation span.
    /// HTTP-out (e.g. HttpClient) and Service Bus receiver/sender operations (receive, send, settle, peek,
    /// lock renewal) are excluded: they are ActivityKind.Client/Producer and are recorded as dependencies,
    /// so they would never correlate to a request.
    /// </summary>
    internal static bool IsInterestingRequest(string requestName)
        => string.Equals(AspNetCoreHttpRequestInName, requestName, StringComparison.Ordinal)
        || string.Equals(ServiceBusProcessMessageName, requestName, StringComparison.Ordinal)
        || string.Equals(ServiceBusProcessSessionMessageName, requestName, StringComparison.Ordinal)
        || string.Equals(FunctionsWorkerInvokeName, requestName, StringComparison.Ordinal)
        || requestName.StartsWith(FunctionsWorkerActivityNamePrefix, StringComparison.Ordinal);

    public void HandleRequestStart(EventWrittenEventArgs eventData, string requestName, string requestId, string operationId, string id)
    {
        Guid currentActivityId = eventData.ActivityId;
        bool isDebugLoggingEnabled = _logger.IsEnabled(LogLevel.Debug);
        if (isDebugLoggingEnabled)
        {
            _logger.LogDebug("Request started: Activity Id: {activityId}", currentActivityId);
        }

        if (!IsInterestingRequest(requestName))
        {
            if (isDebugLoggingEnabled)
            {
                _logger.LogDebug("Drop uninteresting request by name: {requestName}, id: {id}", requestName, id);
            }
            return;
        }

        if (isDebugLoggingEnabled)
        {
            _logger.LogDebug("Interesting start activity, name: {name}, id: {id}", requestName, id);
        }

        // Note to the started-activities bag, so that when stop happens, it knows to match.
        // Multiple handlers (e.g. the OpenTelemetry-Sdk and DiagnosticSource bridges) can both observe
        // the same ASP.NET Core HTTP-in activity. Dedupe so we don't double-emit to the relay EventSource.
        if (!_startedActivityIds.TryAdd(id, default))
        {
            if (isDebugLoggingEnabled)
            {
                _logger.LogDebug("Duplicate start for id {id} (already relayed by another handler); skipping.", id);
            }
            return;
        }

        if (isDebugLoggingEnabled)
        {
            _logger.LogDebug("Set current thread activity id: {activityId}", currentActivityId);
        }
        Guid previousActivityId = EventSource.CurrentThreadActivityId;
        EventSource.SetCurrentThreadActivityId(currentActivityId);
        AzureMonitorOpenTelemetryProfilerDataAdapterEventSource.Log.RequestStart(
            name: requestName,
            id: id,
            requestId: requestId,
            operationId: operationId);
        EventSource.SetCurrentThreadActivityId(previousActivityId);
    }

    public void HandleRequestStop(EventWrittenEventArgs eventData, string requestName, string requestId, string operationId, string id)
    {
        bool isDebugLoggingEnabled = _logger.IsEnabled(LogLevel.Debug);
        Guid currentActivityId = eventData.ActivityId;

        if (isDebugLoggingEnabled)
        {
            _logger.LogDebug("Request stopped: Activity Id: {activityId}", currentActivityId);
        }

        if (!_startedActivityIds.TryRemove(id, out _))
        {
            if (isDebugLoggingEnabled)
            {
                _logger.LogDebug("No start activity found for this stop activity. Request name: {requestName}, id: {id}", requestName, id);
            }
            // No interesting start activity captured, it doesn't make sense to capture the stop alone.
            return;
        }

        if (isDebugLoggingEnabled)
        {
            _logger.LogDebug("Interesting activity found. Name: {name}, id: {id}", requestName, id);
            _logger.LogDebug("Set current thread activity id: {activityId}", currentActivityId);
        }

        Guid previousActivityId = EventSource.CurrentThreadActivityId;
        EventSource.SetCurrentThreadActivityId(currentActivityId);
        AzureMonitorOpenTelemetryProfilerDataAdapterEventSource.Log.RequestStop(
            name: requestName, id: id, requestId: requestId, operationId: operationId);
        EventSource.SetCurrentThreadActivityId(previousActivityId);

        if (Interlocked.CompareExchange(ref _hasActivityReported, 1, 0) == 0 && _logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("Activity detected.");
        }
    }

    // W3C Trace Context id example: 00-4dee62c12eaa9efca3d1f0565f3efda6-b3c470a7ee10c13b-01
    //                                ver  trace-id (operationId)         span-id (requestId) flags
    public static (string requestId, string operationId) ExtractKeyIds(string id)
    {
        string[] tokens = id.Split('-');

        if (tokens.Length != 4 || string.IsNullOrEmpty(tokens[1]) || string.IsNullOrEmpty(tokens[2]))
        {
            throw new InvalidDataException(FormattableString.Invariant($"Id shall have exactly 4 sections separated by `-` with non-empty trace-id and span-id. Actual id: {id}"));
        }

        return (requestId: tokens[2], operationId: tokens[1]);
    }

    /// <summary>
    /// Non-throwing variant of <see cref="ExtractKeyIds"/>. Returns false (with empty out values) when
    /// <paramref name="id"/> is null/empty or not a well-formed W3C trace-context id, instead of throwing.
    /// Used for optional ids (e.g. a parent id) that may be absent.
    /// </summary>
    public static bool TryExtractKeyIds(string? id, out string requestId, out string operationId)
    {
        requestId = string.Empty;
        operationId = string.Empty;

        if (string.IsNullOrEmpty(id))
        {
            return false;
        }

        string[] tokens = id!.Split('-');
        if (tokens.Length != 4 || string.IsNullOrEmpty(tokens[1]) || string.IsNullOrEmpty(tokens[2]))
        {
            return false;
        }

        requestId = tokens[2];
        operationId = tokens[1];
        return true;
    }
}
