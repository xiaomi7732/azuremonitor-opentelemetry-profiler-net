//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using System.Diagnostics;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Extensions.Logging;

namespace Azure.Monitor.OpenTelemetry.Profiler.Core;

internal sealed class DiagnosticsClientTrace : ITraceControl, IDisposable
{
    /// <summary>
    /// How long <see cref="DisableAsync"/> waits for the trace writer to drain the EventPipe stream
    /// after the session has been told to stop. Bounded so a stuck pipe cannot wedge the stop path.
    /// </summary>
    private static readonly TimeSpan TraceWriteDrainTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long <see cref="Dispose"/> waits for the trace writer before closing the EventPipe stream
    /// underneath it. Short, because disposal runs on the shutdown path.
    /// </summary>
    private static readonly TimeSpan DisposeDrainTimeout = TimeSpan.FromSeconds(5);

    public DateTime? SessionStartUTC { get; private set; }

    // Guards _current / _starting / _disposed so that enable, disable and dispose cannot each end up
    // owning the same EventPipe session.
    private readonly object _sessionGate = new();

    // The session currently owned by this instance, together with its in-flight trace writer.
    private TraceSession? _current;
    private bool _starting;
    private bool _disposed;

    private readonly DiagnosticsClientProvider _clientProvider;
    private readonly DiagnosticsClientTraceConfiguration _configuration;
    private readonly ILogger<DiagnosticsClientTrace> _logger;


    public DiagnosticsClientTrace(
        DiagnosticsClientProvider clientProvider,
        DiagnosticsClientTraceConfiguration configuration,
        ILogger<DiagnosticsClientTrace> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _clientProvider = clientProvider ?? throw new ArgumentNullException(nameof(clientProvider));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    /// <inheritdoc />
    public async Task<bool> DisableAsync(CancellationToken cancellationToken = default)
    {
        TraceSession? session = TakeCurrentSession();
        if (session is null)
        {
            _logger.LogWarning("{name} is called when the session doesn't exist.", nameof(DisableAsync));
            return false;
        }

        // From here on this call owns the session, so nothing else can dispose it underneath us.
        session.RequestStop();

        bool stopSent = false;
        bool traceComplete;
        try
        {
            // Stopping only sends the stop command; it does not close the EventPipe stream. The
            // writer keeps draining what the runtime has already buffered, so the trace file is only
            // complete once that writer finishes.
            await session.Session.StopAsync(cancellationToken).ConfigureAwait(false);
            stopSent = true;
        }
        finally
        {
            // Always drain before closing the stream, including when the stop was cancelled or
            // failed: disposing underneath the writer is what truncates the trace and raises the
            // closed-pipe error. Without a stop the stream never reaches EOF, so only wait briefly.
            traceComplete = await session.Writer.WaitAsync(stopSent ? TraceWriteDrainTimeout : DisposeDrainTimeout).ConfigureAwait(false);
            session.Session.Dispose();
        }

        return traceComplete;
    }

    public void Dispose()
    {
        TraceSession? session;
        lock (_sessionGate)
        {
            _disposed = true;
            session = _current;
            _current = null;
        }

        if (session is null)
        {
            return;
        }

        session.RequestStop();

        // Ask the runtime to stop before draining. Without a stop the session keeps streaming, the
        // writer never sees EOF, and the wait below would be a pointless delay that still ends in a
        // truncated trace - which is exactly the shutdown case reported in issue #191.
        try
        {
            session.Session.Stop();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to stop the EventPipe session during disposal.");
        }

        // Bounded: disposal runs on the shutdown path and must not block on a stuck pipe.
        session.Writer.WaitAsync(DisposeDrainTimeout).GetAwaiter().GetResult();
        session.Session.Dispose();
    }

    /// <summary>
    /// Enables a profiler session.
    /// </summary>
    /// <param name="traceFilePath">The trace file path. Default to 'default.nettrace'.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task EnableAsync(string traceFilePath = $"default{OpenTelemetryProfilerProvider.TraceFileExtension}" /* ==> default.nettrace*/, CancellationToken cancellationToken = default)
    {
        // Claim the right to start before awaiting, so two concurrent calls cannot both start a
        // session and leak one of them along with its pipe handle.
        lock (_sessionGate)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(DiagnosticsClientTrace));
            }

            if (_current is not null || _starting)
            {
                throw new InvalidOperationException("Only 1 session at a time is supported. Disable the current session before enabling a new one.");
            }

            _starting = true;
        }

        try
        {
            SessionStartUTC = DateTime.UtcNow;

            using Process currentProcess = Process.GetCurrentProcess();
            int pid = currentProcess.Id;

            EventPipeSession eventPipeSession = await _clientProvider.GetDiagnosticsClient(pid).StartEventPipeSessionAsync(
                providers: _configuration.BuildEventPipeProviders(),
                requestRundown: _configuration.RequestRundown,
                circularBufferMB: _configuration.CircularBufferMB,
                token: cancellationToken).ConfigureAwait(false);

            TraceSession session = new(eventPipeSession, new EventPipeTraceWriter(_logger));

            lock (_sessionGate)
            {
                if (_disposed)
                {
                    // Disposal ran while the session was starting. Tear the new session down here
                    // rather than leaving it live with nobody to stop it.
                    eventPipeSession.Dispose();
                    throw new ObjectDisposedException(nameof(DiagnosticsClientTrace));
                }

                _current = session;
            }

            session.Writer.Start(traceFilePath, eventPipeSession.EventStream);
        }
        finally
        {
            lock (_sessionGate)
            {
                _starting = false;
            }
        }
    }

    /// <summary>
    /// Takes ownership of the current session, so only one caller can stop and dispose it.
    /// </summary>
    private TraceSession? TakeCurrentSession()
    {
        lock (_sessionGate)
        {
            TraceSession? session = _current;
            _current = null;
            return session;
        }
    }

    /// <summary>
    /// An EventPipe session together with the writer draining it.
    /// </summary>
    private sealed class TraceSession
    {
        internal TraceSession(EventPipeSession session, EventPipeTraceWriter writer)
        {
            Session = session;
            Writer = writer;
        }

        public EventPipeSession Session { get; }

        public EventPipeTraceWriter Writer { get; }

        public void RequestStop() => Writer.RequestStop();
    }
}

