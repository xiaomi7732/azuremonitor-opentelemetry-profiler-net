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
    /// after the session has been told to stop. Bounded so a stuck pipe cannot wedge the stop path,
    /// but generous: the post-stop drain is when EventPipe emits rundown, which for a large,
    /// long-running application can take a while. Timing out here drops the profile, so the bound
    /// exists to break a genuinely stuck pipe rather than to cap normal rundown.
    /// </summary>
    internal static readonly TimeSpan DefaultTraceWriteDrainTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How long <see cref="Dispose"/> spends tearing the session down. Short, because disposal runs
    /// on the shutdown path. This is the budget for the whole teardown, not per step.
    /// </summary>
    internal static readonly TimeSpan DefaultDisposeTeardownTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long to wait for the stop command itself. It is a quick IPC round-trip in the normal
    /// case, but can hang on an unresponsive runtime.
    /// </summary>
    internal static readonly TimeSpan DefaultStopCommandTimeout = TimeSpan.FromSeconds(60);

    public DateTime? SessionStartUTC { get; private set; }

    // Guards _current / _starting / _stopping / _disposed so that enable, disable and dispose cannot
    // each end up owning the same EventPipe session.
    private readonly object _sessionGate = new();

    // The session currently owned by this instance, together with its in-flight trace writer.
    private TraceSession? _current;
    private bool _starting;
    private bool _stopping;
    private bool _disposed;

    private readonly TimeSpan _traceWriteDrainTimeout;
    private readonly TimeSpan _disposeTeardownTimeout;
    private readonly TimeSpan _stopCommandTimeout;

    private readonly DiagnosticsClientProvider _clientProvider;
    private readonly DiagnosticsClientTraceConfiguration _configuration;
    private readonly ILogger<DiagnosticsClientTrace> _logger;


    public DiagnosticsClientTrace(
        DiagnosticsClientProvider clientProvider,
        DiagnosticsClientTraceConfiguration configuration,
        ILogger<DiagnosticsClientTrace> logger)
        : this(clientProvider, configuration, logger, DefaultTraceWriteDrainTimeout, DefaultDisposeTeardownTimeout, DefaultStopCommandTimeout)
    {
    }

    /// <summary>
    /// Overload that allows the timeouts to be supplied, so tests are not coupled to the production
    /// wall-clock budgets.
    /// </summary>
    internal DiagnosticsClientTrace(
        DiagnosticsClientProvider clientProvider,
        DiagnosticsClientTraceConfiguration configuration,
        ILogger<DiagnosticsClientTrace> logger,
        TimeSpan traceWriteDrainTimeout,
        TimeSpan disposeTeardownTimeout,
        TimeSpan stopCommandTimeout)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _clientProvider = clientProvider ?? throw new ArgumentNullException(nameof(clientProvider));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _traceWriteDrainTimeout = traceWriteDrainTimeout;
        _disposeTeardownTimeout = disposeTeardownTimeout;
        _stopCommandTimeout = stopCommandTimeout;
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
            // complete once that writer finishes. Bound the command itself: it is a quick IPC
            // round-trip normally, but can hang on an unresponsive runtime.
            using CancellationTokenSource stopCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            stopCancellation.CancelAfter(_stopCommandTimeout);

            await session.Session.StopAsync(stopCancellation.Token).ConfigureAwait(false);
            stopSent = true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Our own stop bound expired, not the caller's cancellation. The session is still torn
            // down below, so this is an incomplete trace rather than a failed stop - faulting here
            // would leave the caller believing the profiler never stopped.
            _logger.LogWarning(
                "Timed out after {timeout} sending the EventPipe stop command. The trace is incomplete and will not be processed.",
                _stopCommandTimeout);
        }
        finally
        {
            try
            {
                // Always drain before closing the stream: disposing underneath the writer is what
                // truncates the trace and raises the closed-pipe error.
                //
                // The drain budget differs by path. When the stop went out, the stream will reach
                // EOF once the runtime has flushed rundown, so wait generously. When it did not,
                // the stop cannot be retried - EventPipeSession marks itself stopped on the first
                // attempt, so a second Stop() sends nothing - and the stream will only end when we
                // close it. Wait briefly in case the endpoint had already gone (which ends the
                // stream on its own), then close.
                traceComplete = await session.Writer
                    .WaitAsync(stopSent ? _traceWriteDrainTimeout : _disposeTeardownTimeout, cancellationToken)
                    .ConfigureAwait(false);

                if (!stopSent)
                {
                    traceComplete = false;
                }

                session.Session.Dispose();
            }
            finally
            {
                // Nested so the session cannot stay marked as stopping if the teardown above throws,
                // which would permanently reject every later EnableAsync.
                lock (_sessionGate)
                {
                    _stopping = false;
                }
            }
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

        if (session is not null)
        {
            StopAndDispose(session, _disposeTeardownTimeout);
        }
    }

    /// <summary>
    /// Best-effort teardown of a session that no caller is going to stop normally: stop it so the
    /// stream can reach EOF, give the writer a chance to drain, then close it. The whole teardown
    /// shares <paramref name="teardownBudget"/>, so a slow stop does not double the time spent here.
    /// </summary>
    private void StopAndDispose(TraceSession session, TimeSpan teardownBudget)
    {
        session.RequestStop();

        Stopwatch elapsed = Stopwatch.StartNew();

        // Stop before draining. Without a stop the session keeps streaming, the writer never sees
        // EOF, and the drain below would be a pointless delay that still ends in a truncated trace -
        // which is exactly the shutdown case reported in issue #191. The stop is a blocking IPC call
        // that the client library warns can hang on an unresponsive runtime, so it is bounded and
        // runs on a dedicated thread: the thread pool can be saturated during shutdown, which would
        // leave a queued stop unstarted.
        Thread stopThread = new(() =>
        {
            try
            {
                session.Session.Stop();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to stop the EventPipe session during teardown.");
            }
        })
        {
            IsBackground = true,
            Name = "EventPipe teardown",
        };

        stopThread.Start();
        if (!stopThread.Join(teardownBudget))
        {
            _logger.LogDebug("Timed out stopping the EventPipe session during teardown.");
        }

        TimeSpan remaining = teardownBudget - elapsed.Elapsed;
        if (remaining > TimeSpan.Zero)
        {
            session.Writer.WaitAsync(remaining).GetAwaiter().GetResult();
        }

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

            if (_current is not null || _starting || _stopping)
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

            // Start the writer before publishing the session, so a concurrent stop or disposal that
            // takes ownership always finds a writer to drain rather than closing the stream against
            // one that has not started yet.
            session.Writer.Start(traceFilePath, eventPipeSession.EventStream);

            bool disposed;
            lock (_sessionGate)
            {
                disposed = _disposed;
                if (!disposed)
                {
                    _current = session;
                }
            }

            if (disposed)
            {
                // Disposal ran while the session was starting. Tear the new session down here rather
                // than leaving it live with nobody to stop it. Stop before disposing: disposing only
                // closes the stream and would leave the runtime still tracing.
                StopAndDispose(session, _disposeTeardownTimeout);
                throw new ObjectDisposedException(nameof(DiagnosticsClientTrace));
            }
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
    /// Takes ownership of the current session, so only one caller can stop and dispose it. The
    /// session stays marked as stopping until the caller finishes tearing it down, so a new one
    /// cannot be started against a session that is still unwinding.
    /// </summary>
    private TraceSession? TakeCurrentSession()
    {
        lock (_sessionGate)
        {
            TraceSession? session = _current;
            _current = null;
            if (session is not null)
            {
                _stopping = true;
            }

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

