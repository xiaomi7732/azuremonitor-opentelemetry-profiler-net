using System.Diagnostics.Tracing;

namespace Azure.Monitor.OpenTelemetry.Profiler.Core.EventListeners;

[EventSource(Name = EventSourceName)]
internal class AzureMonitorOpenTelemetryProfilerDataAdapterEventSource : EventSource
{
    public class Keywords
    {
        public const EventKeywords Operations = (EventKeywords)0x1;
    }

#pragma warning disable CA1724 // Contract by EventSource
#pragma warning disable CA1034 // Contract by EventSource
    public static class Tasks
#pragma warning restore CA1034
#pragma warning restore CA1724
    {
        public const EventTask Request = (EventTask)0x1;
    }

    public const string EventSourceName = "AzureMonitor-OpenTelemetry-Profiler-DataAdapter";

    internal static class EventId
    {
        public const int RequestStart = 1;
        public const int RequestStop = 2;
    }

#pragma warning disable CA2211 // Non-constant fields should not be visible
    public static AzureMonitorOpenTelemetryProfilerDataAdapterEventSource Log = new();
#pragma warning restore CA2211 // Non-constant fields should not be visible

    // ActivityOptions.Disable prevents EventSource's internal ActivityTracker from
    // pushing/popping the thread's ActivityId on WriteEvent, which would cause relay
    // events to appear one level deeper than the bridge events. We keep Opcode.Start/Stop
    // because the uploader's ActivityListValidator matches events by opcode.
    [Event(EventId.RequestStart, Keywords = Keywords.Operations, Opcode = EventOpcode.Start, Task = Tasks.Request, ActivityOptions = EventActivityOptions.Disable)]
    public void RequestStart(string name, string id, string requestId, string operationId)
    {
        WriteEvent(EventId.RequestStart, name, id, requestId, operationId);
    }

    [Event(EventId.RequestStop, Keywords = Keywords.Operations, Opcode = EventOpcode.Stop, Task = Tasks.Request, ActivityOptions = EventActivityOptions.Disable)]
    public void RequestStop(string name, string id, string requestId, string operationId)
    {
        WriteEvent(EventId.RequestStop, name, id, requestId, operationId);
    }
}