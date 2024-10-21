using Microsoft.ApplicationInsights.Profiler.Shared.Contracts;
using Microsoft.ApplicationInsights.Profiler.Shared.Samples;
using Microsoft.ApplicationInsights.Profiler.Shared.Services;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics.Tracing;

namespace Azure.Monitor.OpenTelemetry.Profiler.Core.EventListeners;

internal class SampleCollector : EventListener
{
    static class EventName
    {
        public const string ActivityStarted = nameof(ActivityStarted);
        public const string ActivityStopped = nameof(ActivityStopped);
    }

    private readonly IServiceProfilerContext _serviceProfilerContext;
    private readonly ISerializationProvider _serializer;
    private readonly ILogger<SampleCollector> _logger;
    private readonly ManualResetEventSlim _ctorWaitHandle = new(false);
    private bool _hasActivityReported = false;
    private ConcurrentDictionary<string, SampleActivity> _sampleActivityBuffer = new();

    public SampleActivityContainer SampleActivities { get; }

    private static readonly string _targetEventSourceName = AzureMonitorOpenTelemetryProfilerDataAdapterEventSource.EventSourceName;

    public SampleCollector(
        IServiceProfilerContext serviceProfilerContext,
        SampleActivityContainer sampleActivityContainer,
        ISerializationProvider serializer,
        ILogger<SampleCollector> logger)
    {
        logger.LogTrace("{name} ctor.", nameof(SampleCollector));

        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _serviceProfilerContext = serviceProfilerContext ?? throw new ArgumentNullException(nameof(serviceProfilerContext));
        SampleActivities = sampleActivityContainer ?? throw new ArgumentNullException(nameof(sampleActivityContainer));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _ctorWaitHandle.Set();
        logger.LogTrace("{name} created.", nameof(SampleCollector));
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
            if (!string.Equals(eventData.EventSource.Name, _targetEventSourceName, StringComparison.Ordinal))
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

        if (string.Equals(eventData.EventSource.Name, _targetEventSourceName, StringComparison.Ordinal) && (
            eventData.EventId == AzureMonitorOpenTelemetryProfilerDataAdapterEventSource.EventId.RequestStart ||
            eventData.EventId == AzureMonitorOpenTelemetryProfilerDataAdapterEventSource.EventId.RequestStop))
        {
            string requestName = eventData.GetPayload<string>("name") ?? "Unknown";
            string spanId = eventData.GetPayload<string>("id") ?? throw new InvalidDataException("id payload is missing.");
            string requestId = eventData.GetPayload<string>("requestId") ?? throw new InvalidDataException("requestId is missing.");
            string operationId = eventData.GetPayload<string>("operationId") ?? throw new InvalidDataException("operationId is missing.");

            Guid activityId = eventData.ActivityId;
            string activityIdPath = activityId.GetActivityPath();
            _logger.LogDebug("{activityId} => {activityIdPath}, requestName: {requestName}, spanId: {spanId}, requestId: {requestId}, operationId: {operationId}",
                activityId, activityIdPath, requestName, spanId, requestId, operationId);

            if (eventData.EventId == AzureMonitorOpenTelemetryProfilerDataAdapterEventSource.EventId.RequestStart) // Started
            {
                HandleRequestStart(eventData, activityIdPath, requestId, operationId);
                return;
            }

            if (eventData.EventId == AzureMonitorOpenTelemetryProfilerDataAdapterEventSource.EventId.RequestStop) // Stopped
            {
                HandleRequestStop(eventData, requestName, activityIdPath, requestId);
                return;
            }

            _logger.LogWarning("Unhandled event id: {eventId}", eventData.EventId);
        }
    }

    private async Task HandleEventSourceCreated(EventSource eventSource)
    {
        // This has to be the very first statement to making sure the objects are constructured.
        _ctorWaitHandle.Wait();

        try
        {
            _logger.LogInformation("Got manual trigger for source: {name}", eventSource.Name);

            await Task.Yield();
            if (string.Equals(eventSource.Name, _targetEventSourceName, StringComparison.OrdinalIgnoreCase))
            {
                EventKeywords keywordsMask = (EventKeywords)0x0000F;
                _logger.LogDebug("Enabling EventSource: {eventSourceName}", eventSource.Name);
                EnableEvents(eventSource, EventLevel.Verbose, keywordsMask);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error enalbling event source. {name}", eventSource.Name);
        }
    }

    private void HandleRequestStart(EventWrittenEventArgs eventData, string activityIdPath, string requestId, string operationId)
    {
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

    private void HandleRequestStop(EventWrittenEventArgs eventData, string requestName, string activityIdPath, string requestId)
    {
        if (_sampleActivityBuffer.TryRemove(requestId, out SampleActivity? activity))
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