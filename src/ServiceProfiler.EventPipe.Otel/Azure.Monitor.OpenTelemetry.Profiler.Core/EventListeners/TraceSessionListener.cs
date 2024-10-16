using System.Diagnostics.Tracing;
using Microsoft.Extensions.Logging;

namespace Azure.Monitor.OpenTelemetry.Profiler.Core.EventListeners;

internal class TraceSessionListener : EventListener
{
    static class EventName
    {
        public const string ActivityStarted = nameof(ActivityStarted);
        public const string ActivityStopped = nameof(ActivityStopped);
    }

    public const string OpenTelemetrySDKEventSourceName = "OpenTelemetry-Sdk";
    private readonly ILogger<TraceSessionListener> _logger;
    private readonly ManualResetEventSlim _ctorWaitHandle = new(false);

    public TraceSessionListener(ILogger<TraceSessionListener> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _logger.LogInformation("Trace session listener ctor.");
        _ctorWaitHandle.Set();
        _logger.LogInformation("Trace session listener created.");
    }

    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        // This event might tirgger before the constructor is done.
        TryLogInfo($"Event source creating: {eventSource.Name}");
        // Dispatch this onto a different thread to avoid holding the thread to finish 
        // the constructor
        Task.Run(() =>
        {
            _ = HandleEventSourceCreated(eventSource).ConfigureAwait(false);
        });
        TryLogInfo($"Event source created: {eventSource.Name}");
    }

    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        _logger.LogTrace("OnEventWritten by {eventSource}", eventData.EventSource.Name);
        base.OnEventWritten(eventData);

        try
        {
            if (
                !string.Equals(eventData.EventSource.Name, OpenTelemetrySDKEventSourceName, StringComparison.Ordinal) &&
                !string.Equals(eventData.EventSource.Name, "OpenTelemetry-AzureMonitor-AspNetCore", StringComparison.Ordinal))
            {
                return;
            }

            OnRichPayloadEventWritten(eventData);
        }
        catch (Exception ex)
        {
            // We don't expect any exception here but if it happens, we still want to catch it and log it.
            // However, we don't want this to break the user's application.
            _logger.LogError(ex, "Unexpected exception happened.");
        }
    }

    /// <summary>
    /// Parses the rich payload EventSource event, adapter it and pump it into the Relay EventSource.
    /// </summary>
    /// <param name="eventData"></param>
    public void OnRichPayloadEventWritten(EventWrittenEventArgs eventData)
    {
        _logger.LogTrace("{Action} - ActivityId: {activityId}, EventName: {eventName}, Keywords: {keyWords}, OpCode: {opCode}",
            nameof(OnRichPayloadEventWritten),
            eventData.ActivityId,
            eventData.EventName,
            eventData.Keywords,
            eventData.Opcode);

        if (string.IsNullOrEmpty(eventData.EventName))
        {
            return;
        }

        object? payload = eventData.Payload;
        if (payload is null)
        {
            _logger.LogWarning("Unexpected empty payload.");
            return;
        }

        string requestName = eventData.GetPayload<string>("name") ?? "Unknown";
        string? rawId = eventData.GetPayload<string>("id");

        if (eventData.EventId == 24) // Started
        {
            _logger.LogInformation("Requet started: Activity Id: {activityId}", eventData.ActivityId);
            AzureMonitorOpenTelemetryProfilerDataAdapterEventSource.Log.RequestStart(requestName);
        }

        if (eventData.EventId == 25) // Stopped
        {
            _logger.LogInformation("Requet stopped: Activity Id: {activityId}", eventData.ActivityId);
            AzureMonitorOpenTelemetryProfilerDataAdapterEventSource.Log.RequestStop(requestName);
        }

        // if (eventData.EventName.Equals(EventName.Request, StringComparison.Ordinal) && (eventData.Keywords.HasFlag(ApplicationInsightsDataRelayEventSource.Keywords.Operations)))
        // {

        //     // Operation is sent, handle Start and Stop for it.
        //     ApplicationInsightsOperationEvent operationEventData = eventData.ToAppInsightsOperationEvent(_serializer);
        //     _logger.LogTrace("Request Activity (Start or Stop). Request Id: {requestId}.", operationEventData.RequestId);
        //     if (eventData.Opcode == EventOpcode.Start)
        //     {
        //         Guid startActivityId = eventData.ActivityId;
        //         RelayStartRequest(operationEventData, startActivityId);
        //         // Getting activity id post relay
        //         string startActivityPath = startActivityId.GetActivityPath();

        //         // Record start time utc and start activity id.
        //         SampleActivity result = _sampleActivityBuffer.AddOrUpdate(operationEventData.RequestId, new SampleActivity()
        //         {
        //             StartActivityIdPath = startActivityPath,
        //             StartTimeUtc = operationEventData.TimeStamp,
        //             RequestId = operationEventData.RequestId,
        //             OperationId = operationEventData.OperationId,
        //         }, (key, value) =>
        //         {
        //             value.StartActivityIdPath = startActivityPath;
        //             value.StartTimeUtc = operationEventData.TimeStamp;
        //             value.RequestId = operationEventData.RequestId;
        //             Debug.Assert(string.Equals(value.OperationId, operationEventData.OperationId, StringComparison.Ordinal),
        //                 $"Start/Stop activity operation ids ({value.OperationId}/{operationEventData.OperationId}) should be the same.");
        //             return value;
        //         });

        //         if (result == null)
        //         {
        //             _logger.LogWarning("Failed adding start activity: {0}, request id: {1}", startActivityPath, operationEventData.RequestId);
        //         }
        //     }
        //     else if (eventData.Opcode == EventOpcode.Stop)
        //     {
        //         // Getting activity id before relay.
        //         Guid stopActivityId = eventData.ActivityId;
        //         string stopActivityPath = stopActivityId.GetActivityPath();

        //         SampleActivity result = _sampleActivityBuffer.AddOrUpdate(
        //             operationEventData.RequestId,
        //             new SampleActivity()
        //             {
        //                 StopActivityIdPath = stopActivityPath,
        //                 StopTimeUtc = operationEventData.TimeStamp,
        //                 RequestId = operationEventData.RequestId,
        //                 OperationId = operationEventData.OperationId,
        //             }, (key, value) =>
        //             {
        //                 value.StopActivityIdPath = stopActivityPath;
        //                 value.StopTimeUtc = operationEventData.TimeStamp;
        //                 value.RequestId = operationEventData.RequestId;
        //                 Debug.Assert(string.Equals(value.OperationId, operationEventData.OperationId, StringComparison.Ordinal),
        //                     $"Start/Stop activity operation ids ({value.OperationId}/{operationEventData.OperationId}) should be the same.");
        //                 return value;
        //             });

        //         if (result != null)
        //         {
        //             RelayStopRequest(operationEventData, result.StartTimeUtc.UtcTicks, stopActivityId);
        //         }
        //     }
        // }
        // else if (eventData.EventName.Equals(EventName.Request, StringComparison.Ordinal) && (eventData.Keywords.HasFlag(ApplicationInsightsDataRelayEventSource.Keywords.Request)))
        // {
        //     var requestEventData = eventData.ToAppInsightsRequestEvent(_serializer, _serializerOptionsProvider);
        //     _logger.LogTrace("Request Activity. Request Id: {requestId}.", requestEventData.RequestId);

        //     SampleActivity targetRequest;
        //     if (_sampleActivityBuffer.TryRemove(requestEventData.RequestId, out targetRequest))
        //     {
        //         targetRequest.OperationName = requestEventData.OperationName;
        //         targetRequest.Duration = requestEventData.Duration;
        //         targetRequest.RoleInstance = Environment.MachineName;

        //         AppendSampleActivity(targetRequest);
        //     }
        //     else
        //     {
        //         string message = "There is no matched start activity found for this request id: {0}. This could happen for the first few activities.";
        //         var requestId = requestEventData.RequestId;
        //         if (!_hasActivityReported)
        //         {
        //             _logger.LogInformation(message, requestId);
        //             _hasActivityReported = true;
        //         }
        //         else
        //         {
        //             _logger.LogDebug(message, requestId);
        //         }
        //     }
        // }
    }

    private async Task HandleEventSourceCreated(EventSource eventSource)
    {
        // This has to be the very first statement to making sure the objects are constructured.
        _ctorWaitHandle.Wait();

        try
        {
            // await Task.Delay(TimeSpan.FromSeconds(5));
            // _logger.LogInformation("Waiting manual trigger for source: {name}", eventSource.Name);
            // So that the rest of the code doesn't run before the end of the constructor
            _logger.LogInformation("Got manual trigger for source: {name}", eventSource.Name);

            await Task.Yield();
            if (string.Equals(eventSource.Name, OpenTelemetrySDKEventSourceName, StringComparison.OrdinalIgnoreCase))
            {
                EventKeywords keywordsMask = (EventKeywords)0x0000F;
                _logger.LogDebug("Enabling EventSource: {eventSourceName}", eventSource.Name);
                EnableEvents(eventSource, EventLevel.Verbose, keywordsMask);
            }
            else if (eventSource.Name == "System.Threading.Tasks.TplEventSource")
            {
                // Activity IDs aren't enabled by default.
                // Enabling Keyword 0x80 on the TplEventSource turns them on
                EnableEvents(eventSource, EventLevel.LogAlways, (EventKeywords)0x80);
            }
        }
        catch (Exception ex)
        {
            if (_logger is null)
            {
                Console.WriteLine("Error enabling event source. Details: {0}", ex.ToString());
            }
            _logger?.LogError(ex, "Error enalbling ");
        }
    }

    public override void Dispose()
    {
        _ctorWaitHandle.Dispose();
        base.Dispose();
    }

    private void TryLogInfo(string message)
    {
        if (_logger is null)
        {
            Console.WriteLine(message);
            return;
        }
        _logger.LogInformation(message);
    }
}