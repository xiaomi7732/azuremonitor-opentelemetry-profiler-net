using Microsoft.ApplicationInsights.Profiler.Shared.Contracts;
using Microsoft.ApplicationInsights.Profiler.Shared.Samples;
using Microsoft.ApplicationInsights.Profiler.Shared.Services;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics.Tracing;

namespace Azure.Monitor.OpenTelemetry.Profiler.Core.EventListeners;

internal class TraceSessionListener : EventListener
{
    static class EventName
    {
        public const string ActivityStarted = nameof(ActivityStarted);
        public const string ActivityStopped = nameof(ActivityStopped);
    }

    public const string OpenTelemetrySDKEventSourceName = "OpenTelemetry-Sdk";
    private readonly IServiceProfilerContext _serviceProfilerContext;
    private readonly ISerializationProvider _serializer;
    private readonly ILogger<TraceSessionListener> _logger;
    private readonly ManualResetEventSlim _ctorWaitHandle = new(false);
    private bool _hasActivityReported = false;
    private ConcurrentDictionary<string, SampleActivity> _sampleActivityBuffer = new();
    
    public SampleActivityContainer SampleActivities { get; }

    public TraceSessionListener(
        IServiceProfilerContext serviceProfilerContext,
        SampleActivityContainer sampleActivityContainer,
        ISerializationProvider serializer,
        ILogger<TraceSessionListener> logger)
    {
        logger.LogTrace("Trace session listener ctor.");
        
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _serviceProfilerContext = serviceProfilerContext ?? throw new ArgumentNullException(nameof(serviceProfilerContext));
        SampleActivities = sampleActivityContainer ?? throw new ArgumentNullException(nameof(sampleActivityContainer));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _ctorWaitHandle.Set();
        logger.LogTrace("Trace session listener created.");
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
            if (!string.Equals(eventData.EventSource.Name, OpenTelemetrySDKEventSourceName, StringComparison.Ordinal))
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
        _logger.LogTrace("[{Source}] {Action} - ActivityId: {activityId}, EventName: {eventName}, Keywords: {keyWords}, OpCode: {opCode}",
            eventData.EventSource.Name,
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

        if (string.Equals(eventData.EventSource.Name, OpenTelemetrySDKEventSourceName, StringComparison.Ordinal) && eventData.EventId == 24 || eventData.EventId == 25)
        {
            string requestName = eventData.GetPayload<string>("name") ?? "Unknown";
            string spanId = eventData.GetPayload<string>("id") ?? throw new InvalidDataException("id payload is missing.");
            string activityIdPath = eventData.ActivityId.GetActivityPath();
            (string operationId, string requestId) = ExtractKeyIds(spanId);

            if (eventData.EventId == 24) // Started
            {
                HandleRequestStart(eventData, requestName, activityIdPath, requestId, operationId);
                return;
            }

            if (eventData.EventId == 25) // Stopped
            {
                HandleRequestStop(eventData, requestName, activityIdPath, requestId);
                return;
            }
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

    private void HandleRequestStop(EventWrittenEventArgs eventData, string requestName, string activityIdPath, string requestId)
    {
        _logger.LogInformation("Requet stopped: Activity Id: {activityId}", eventData.ActivityId);
        AzureMonitorOpenTelemetryProfilerDataAdapterEventSource.Log.RequestStop(requestName);

        SampleActivity? activity;
        if (_sampleActivityBuffer.TryRemove(requestId, out activity))
        {
            activity.OperationName = requestName;
            activity.StopTimeUtc = eventData.TimeStamp;
            activity.Duration = eventData.TimeStamp - activity.StartTimeUtc;
            activity.RoleInstance = _serviceProfilerContext.MachineName;
            activity.StopActivityIdPath = activityIdPath;

            AppendSampleActivity(activity);
        }
        else
        {
            string message = "There is no matched start activity found for this request id: {requestId}. This could happen for the first few activities.";
            if (!_hasActivityReported)
            {
                _logger.LogInformation(message, requestId);
                _hasActivityReported = true;
            }
            else
            {
                _logger.LogDebug(message, requestId);
            }
        }
    }

    private void AppendSampleActivity(SampleActivity activity)
    {
        if (activity.IsValid(_logger))
        {
            // Send the AI CustomEvent
            try
            {
                if (SampleActivities.AddSample(activity))
                {
                    if (_logger.IsEnabled(LogLevel.Debug))  // Perf: Avoid serialization when not debugging.
                    {
                        bool isActivitySerialized = _serializer.TrySerialize(activity, out string? serializedActivity);
                        if (isActivitySerialized)
                        {
                            _logger.LogDebug("Sample is added: {0}", serializedActivity);
                        }
                        else
                        {
                            _logger.LogWarning("Serialize failed for activity: {0}", activity?.OperationId);
                        }
                    }
                }
                else
                {
                    _logger.LogError("Fail to add activity into collection. Please making sure there's enough memory.");
                }
            }
            catch (ObjectDisposedException ex)
            {
                // activity builder has been disposed.
                _logger.LogError(ex, "Start activity cache has been disposed before the activity is recorded.");
            }
            catch (InvalidOperationException ex)
            {
                // The underlying collection was modified outside of this BlockingCollection<T> instance.
                _logger.LogError(ex, "Invalid operation on start activity cache. Fail to record the activity.");
            }
        }
        else
        {
            _logger.LogInformation("Target request data is not valid upon receiving requests: {requestId}. This could happen for the first few activities.", activity.RequestId);
        }
    }

    private void HandleRequestStart(EventWrittenEventArgs eventData, string requestName, string activityIdPath, string requestId, string operationId)
    {
        _logger.LogInformation("Requet started: Activity Id: {activityId}", eventData.ActivityId);
        AzureMonitorOpenTelemetryProfilerDataAdapterEventSource.Log.RequestStart(requestName);

        // Record start time utc and start activity id.
        SampleActivity result = _sampleActivityBuffer.AddOrUpdate(requestId, new SampleActivity()
        {
            StartActivityIdPath = activityIdPath,
            StartTimeUtc = eventData.TimeStamp,
            RequestId = requestId,
            OperationId = operationId,
        }, (key, value) =>
        {
            value.StartActivityIdPath = activityIdPath;
            value.StartTimeUtc = eventData.TimeStamp;
            value.RequestId = requestId;
            value.OperationId = operationId;
            return value;
        });
    }

    private void HandleRequestStart(EventWrittenEventArgs eventArgs)
    {

    }

    private async Task HandleEventSourceCreated(EventSource eventSource)
    {
        // This has to be the very first statement to making sure the objects are constructured.
        _ctorWaitHandle.Wait();

        try
        {
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

    // span id example: 00-4dee62c12eaa9efca3d1f0565f3efda6-b3c470a7ee10c13b-01
    private (string operationId, string requestId) ExtractKeyIds(string spanId)
    {
        string[] tokens = spanId.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length != 4)
        {
            throw new InvalidDataException(FormattableString.Invariant($"Span id shall have 4 sections separated by `-`. Actual: {spanId}"));
        }
        return (tokens[1], tokens[2]);
    }
}