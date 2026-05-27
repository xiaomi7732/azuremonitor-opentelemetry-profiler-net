using System.Diagnostics;
using System.Diagnostics.Tracing;
using Microsoft.Extensions.Logging;

namespace Azure.Monitor.OpenTelemetry.Profiler.Core.EventListeners;

/// <summary>
/// Registers an <see cref="ActivityListener"/> for Azure Service Bus processor ActivitySources
/// that resets the thread-local EventSource ActivityId to <see cref="Guid.Empty"/> in the
/// <see cref="ActivityListener.Sample"/> callback.
/// </summary>
/// <remarks>
/// <para>
/// This fixes a nesting issue in the <c>DiagnosticSourceEventSource</c> bridge's
/// <c>Activity1Start</c>/<c>Activity1Stop</c> events. The bridge uses EventSource's thread-local
/// ActivityId with <see cref="EventOpcode.Start"/> to push a child ActivityId. For async processing
/// (like Service Bus), the <c>Activity1Stop</c> fires on a different thread, so the Start thread's
/// ActivityId is never popped. When the thread is reused for the next message, the bridge pushes
/// under the stale (un-popped) parent, creating a nested tree instead of flat siblings.
/// </para>
/// <para>
/// The reset is performed in the <see cref="ActivityListener.Sample"/> callback rather than
/// <see cref="ActivityListener.ActivityStarted"/> because the runtime guarantees that ALL
/// <c>Sample</c> callbacks complete before ANY <c>ActivityStarted</c> callback fires.
/// <c>ActivityStarted</c> callbacks fire in reverse registration order (last-registered first),
/// so the bridge's <c>ActivityStarted</c> — which writes the <c>Activity1Start</c> EventSource
/// event — fires before ours. Resetting in <c>ActivityStarted</c> would be too late.
/// </para>
/// </remarks>
internal sealed class ServiceBusActivityIdResetListener : IDisposable
{
    private readonly ActivityListener _listener;

    public ServiceBusActivityIdResetListener(ILogger<ServiceBusActivityIdResetListener> logger)
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = static source =>
                source.Name == "Azure.Messaging.ServiceBus.ServiceBusProcessor" ||
                source.Name == "Azure.Messaging.ServiceBus.ServiceBusSessionProcessor",

            // Reset the thread-local EventSource ActivityId in the Sample callback,
            // NOT in ActivityStarted. The runtime guarantees that ALL Sample callbacks
            // complete before ANY ActivityStarted callback fires. ActivityStarted
            // callbacks fire in reverse registration order (last-registered first),
            // so the bridge's ActivityStarted fires before ours — too late to reset.
            // Sample callbacks also fire in reverse order, but since the bridge's
            // Sample has no thread-state side effects, our reset in Sample is
            // effective regardless of ordering.
            Sample = static (ref ActivityCreationOptions<ActivityContext> options) =>
            {
                EventSource.SetCurrentThreadActivityId(Guid.Empty);
                return ActivitySamplingResult.AllDataAndRecorded;
            },

            SampleUsingParentId = static (ref ActivityCreationOptions<string> options) =>
            {
                EventSource.SetCurrentThreadActivityId(Guid.Empty);
                return ActivitySamplingResult.AllDataAndRecorded;
            },
        };

        ActivitySource.AddActivityListener(_listener);
        logger.LogDebug("Registered ActivityListener to reset EventSource ActivityId for Service Bus processor activities.");
    }

    public void Dispose() => _listener.Dispose();
}
