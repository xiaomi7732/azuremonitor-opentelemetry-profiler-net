#define CONSOLE_LOGGING
#undef CONSOLE_LOGGING    // Comment out this line for traces before the finishing of the constructor

using Microsoft.ApplicationInsights.Profiler.Shared.Samples;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
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
    public const string OpenTelemetrySDKEventSourceName = OpenTelemetrySdkEventSourceHandler.EventSourceName;
    public const string DiagnosticSourceEventSourceName = DiagnosticSourceEventSourceHandler.EventSourceName;

    private readonly ISerializationProvider _serializer;
    private readonly SampleCollector _sampleCollector;
    private readonly ILogger<TraceSessionListener> _logger;
    private readonly IReadOnlyList<IEventSourceHandler> _handlers;
    private readonly ManualResetEventSlim _ctorWaitHandle = new(false);

    public SampleActivityContainer? SampleActivities => _sampleCollector?.SampleActivities;

    public TraceSessionListener(
        ISerializationProvider serializer,
        SampleCollector sampleCollector,
        IEnumerable<IEventSourceHandler> handlers,
        ILogger<TraceSessionListener> logger)
    {
        logger.LogTrace("Trace session listener ctor.");

        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _sampleCollector = sampleCollector ?? throw new ArgumentNullException(nameof(sampleCollector));
        _handlers = (handlers ?? throw new ArgumentNullException(nameof(handlers))).ToArray();
        _ctorWaitHandle.Set();
        logger.LogTrace("Trace session listener created.");
    }

    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        // This event might trigger before the constructor is done.
        TryLogDebug($"Event source creating: {eventSource.Name}");
        // Dispatch onto a different thread to avoid holding the thread that is finishing the constructor.
        Task.Run(() =>
        {
            _ = HandleEventSourceCreatedAsync(eventSource).ConfigureAwait(false);
        });
        TryLogDebug($"Event source created: {eventSource.Name}");
    }

    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        _logger.LogTrace("OnEventWritten by {eventSource}", eventData.EventSource.Name);

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
        // This has to be the very first statement to ensure handlers/logger are constructed.
        _ctorWaitHandle.Wait();

        try
        {
            _logger.LogDebug("Got manual trigger for source: {name}", eventSource.Name);

            await Task.Yield();

            foreach (IEventSourceHandler handler in _handlers)
            {
                if (handler.CanHandle(eventSource))
                {
                    handler.Enable(this, eventSource);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error enabling event source: {name}", eventSource.Name);
        }
    }

    public override void Dispose()
    {
        _ctorWaitHandle.Dispose();
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
