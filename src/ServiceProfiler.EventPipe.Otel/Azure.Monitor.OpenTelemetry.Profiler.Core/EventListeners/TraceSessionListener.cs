// Uncomment the following line to enable Console-based fallback logging for early ctor traces.
// #define CONSOLE_LOGGING

using Microsoft.ApplicationInsights.Profiler.Shared.Samples;
using Microsoft.Extensions.Logging;
using System.Diagnostics.Tracing;

namespace Azure.Monitor.OpenTelemetry.Profiler.Core.EventListeners;

/// <summary>
/// Thin <see cref="EventListener"/> that dispatches to a list of <see cref="IEventSourceHandler"/>
/// strategies. Each handler owns the source-specific knowledge (which source to bind to, which
/// keywords / FilterAndPayloadSpecs to enable it with, how to interpret incoming events).
/// </summary>
internal class TraceSessionListener : EventListener
{
    // Kept here as the public surface relied on by DiagnosticsClientTraceConfiguration and similar.
    // Note: SDK casing preserved for backward compatibility with existing references.
    public const string OpenTelemetrySDKEventSourceName = OpenTelemetrySdkEventSourceHandler.EventSourceName;
    public const string DiagnosticSourceEventSourceName = DiagnosticSourceEventSourceHandler.EventSourceName;

    private readonly SampleCollector _sampleCollector;
    private readonly ILogger<TraceSessionListener> _logger;
    // Typed as array (not IReadOnlyList<>) so the foreach in OnEventWritten uses the no-alloc
    // array enumerator — this is a hot path.
    private readonly IEventSourceHandler[] _handlers;
    // Owned for disposal — resets EventSource ActivityId for async Service Bus activities.
    private readonly ServiceBusActivityIdResetListener _resetListener;
    // Field initializer (runs before the base EventListener ctor) — important because the base
    // ctor synchronously dispatches OnEventSourceCreated for every existing EventSource, and
    // those callbacks rely on this handle being non-null.
    private readonly ManualResetEventSlim _ctorWaitHandle = new(false);
    private volatile bool _disposed;

    public SampleActivityContainer? SampleActivities => _sampleCollector?.SampleActivities;

    public TraceSessionListener(
        SampleCollector sampleCollector,
        IEnumerable<IEventSourceHandler> handlers,
        ServiceBusActivityIdResetListener resetListener,
        ILogger<TraceSessionListener> logger)
    {
        logger.LogTrace("Trace session listener ctor.");

        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _sampleCollector = sampleCollector ?? throw new ArgumentNullException(nameof(sampleCollector));
        _resetListener = resetListener ?? throw new ArgumentNullException(nameof(resetListener));
        try
        {
            _handlers = (handlers ?? throw new ArgumentNullException(nameof(handlers))).ToArray();
            logger.LogTrace("Trace session listener created.");
        }
        finally
        {
            _ctorWaitHandle.Set();
        }
    }

    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        // This event might trigger before the constructor is done.
        TryLogDebug($"Event source creating: {eventSource.Name}");
        // HandleEventSourceCreatedAsync detaches from this thread on its first await; no Task.Run needed.
        // The method swallows all its own exceptions, so the discarded Task can't escape unobserved.
        _ = HandleEventSourceCreatedAsync(eventSource);
        TryLogDebug($"Event source created: {eventSource.Name}");
    }

    /// <summary>
    /// Dispatches each event to the first handler whose <see cref="IEventSourceHandler.CanHandle"/>
    /// returns true. EventSource names are unique, so first-match is sufficient.
    /// </summary>
    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        try
        {
            // Handlers are few; a linear scan is cheaper than a dictionary lookup.
            foreach (IEventSourceHandler handler in _handlers)
            {
                if (handler.CanHandle(eventData.EventSource))
                {
                    handler.OnEventWritten(eventData);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            // We don't expect any exception here but if it happens, we still want to catch it and log it.
            // However, we don't want this to break the user's application.
            _logger.LogError(ex, "Unexpected exception happened.");
        }
    }

    private async Task HandleEventSourceCreatedAsync(EventSource eventSource)
    {
        // Yield FIRST so we detach from the caller's thread before doing anything that could
        // block. The caller may be the base EventListener ctor (enumerating pre-existing
        // EventSources synchronously) — blocking on _ctorWaitHandle on that thread would
        // deadlock, since only our derived ctor body (which hasn't run yet) can Set the handle.
        await Task.Yield();

        try
        {
            // Wait until the derived constructor is finished — but bail out cheaply if we've been
            // disposed before the handle is signaled (avoids ObjectDisposedException on the wait).
            if (_disposed)
            {
                return;
            }
            _ctorWaitHandle.Wait();
            if (_disposed)
            {
                return;
            }

            _logger.LogDebug("Got manual trigger for source: {name}", eventSource.Name);

            foreach (IEventSourceHandler handler in _handlers)
            {
                // Re-check before each Enable — Dispose may race with us and tear down the listener.
                if (_disposed)
                {
                    return;
                }
                if (handler.CanHandle(eventSource))
                {
                    handler.Enable(this, eventSource);
                    return;
                }
            }
        }
        catch (ObjectDisposedException)
        {
            // Raced with Dispose() — ignore.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enabling event source: {name}", eventSource.Name);
        }
    }

    public override void Dispose()
    {
        _disposed = true;
        // Signal any waiter so it can observe _disposed and exit promptly.
        try
        {
            _ctorWaitHandle.Set();
        }
        catch (ObjectDisposedException)
        {
        }
        _ctorWaitHandle.Dispose();
        _resetListener.Dispose();
        base.Dispose();
    }

    private void TryLogDebug(string message)
    {
        if (_logger is null)
        {
#if CONSOLE_LOGGING
            Console.WriteLine(message);
#endif
            return;
        }
        _logger.LogDebug(message);
    }
}
