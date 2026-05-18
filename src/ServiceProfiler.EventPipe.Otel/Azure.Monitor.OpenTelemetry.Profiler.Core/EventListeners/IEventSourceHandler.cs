using System.Diagnostics.Tracing;

namespace Azure.Monitor.OpenTelemetry.Profiler.Core.EventListeners;

/// <summary>
/// Strategy for handling a single <see cref="EventSource"/> from inside <see cref="TraceSessionListener"/>.
/// Each handler owns the source-specific knowledge (which source name to bind, which keywords / payload spec
/// to enable it with, and how to interpret incoming events) so that the listener stays a thin dispatcher.
/// </summary>
internal interface IEventSourceHandler
{
    /// <summary>
    /// Returns true when this handler wants to enable and consume events from <paramref name="eventSource"/>.
    /// Typically a name match.
    /// </summary>
    bool CanHandle(EventSource eventSource);

    /// <summary>
    /// Enables the source on the supplied listener. Called from <see cref="EventListener.OnEventSourceCreated"/>.
    /// </summary>
    void Enable(EventListener listener, EventSource eventSource);

    /// <summary>
    /// Interprets one event written by a previously-enabled source.
    /// </summary>
    void OnEventWritten(EventWrittenEventArgs eventData);
}
