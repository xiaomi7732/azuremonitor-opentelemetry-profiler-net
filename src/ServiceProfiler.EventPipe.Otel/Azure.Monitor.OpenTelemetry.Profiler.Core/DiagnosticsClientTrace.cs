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
    private EventPipeSession? _session;

    // The in-flight trace writer, retained so stopping and disposal can coordinate with it. Disposing
    // the session closes the EventPipe stream, so letting that race the writer both truncates the
    // trace file and surfaces a closed-pipe ObjectDisposedException.
    private Task<bool>? _traceWriteTask;

    // Set before the EventPipe session is stopped or disposed, so the writer can tell an expected
    // lifecycle-induced closed pipe from a genuine failure.
    private volatile bool _stopRequested;

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
        EventPipeSession? session = _session;
        if (session is null)
        {
            _logger.LogWarning("{name} is called when the session doesn't exist.", nameof(DisableAsync));
            return false;
        }

        RequestStop();

        try
        {
            // Stopping only sends the stop command; it does not close the EventPipe stream. The
            // writer keeps draining what the runtime has already buffered, so the trace file is only
            // complete once that writer finishes.
            await session.StopAsync(cancellationToken).ConfigureAwait(false);
            return await WaitForTraceWriteAsync(TraceWriteDrainTimeout).ConfigureAwait(false);
        }
        finally
        {
            // Dispose only after the writer has settled: disposing closes the stream out from under it.
            session.Dispose();
            _session = null;
            _traceWriteTask = null;
            _stopRequested = false;
        }
    }

    public void Dispose()
    {
        // Disposal closes the EventPipe stream. Give an in-flight writer a brief chance to finish so
        // it does not fail with a closed pipe, but never block shutdown on it.
        RequestStop();
        WaitForTraceWriteAsync(DisposeDrainTimeout).GetAwaiter().GetResult();

        _session?.Dispose();
        _session = null;
        _traceWriteTask = null;
    }

    /// <summary>
    /// Marks the session as stopping, so a closed EventPipe stream is treated as an expected
    /// lifecycle outcome by the trace writer rather than as a failure.
    /// </summary>
    internal void RequestStop() => _stopRequested = true;

    /// <summary>
    /// Enables a profiler session.
    /// </summary>
    /// <param name="traceFilePath">The trace file path. Default to 'default.nettrace'.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns></returns>
    public async Task EnableAsync(string traceFilePath = $"default{OpenTelemetryProfilerProvider.TraceFileExtension}" /* ==> default.nettrace*/, CancellationToken cancellationToken = default)
    {
        if (_session is not null)
        {
            throw new InvalidOperationException("Only 1 session at a time is supported. Disable the current session before enabling a new one.");
        }

        SessionStartUTC = DateTime.UtcNow;

        using Process currentProcess = Process.GetCurrentProcess();
        int pid = currentProcess.Id;

        EventPipeSession session = await _clientProvider.GetDiagnosticsClient(pid).StartEventPipeSessionAsync(
            providers: _configuration.BuildEventPipeProviders(),
            requestRundown: _configuration.RequestRundown,
            circularBufferMB: _configuration.CircularBufferMB,
            token: cancellationToken).ConfigureAwait(false);

        _session = session;
        BeginTraceWrite(traceFilePath, session.EventStream);
    }

    /// <summary>
    /// Starts copying the EventPipe stream to the trace file and retains the resulting task so that
    /// stopping and disposal can wait for it.
    /// </summary>
    internal void BeginTraceWrite(string traceFilePath, Stream stream)
    {
        _stopRequested = false;
        _traceWriteTask = WriteTraceAsync(traceFilePath, stream);
    }

    /// <summary>
    /// Waits for the in-flight trace writer to finish within <paramref name="timeout"/>.
    /// </summary>
    /// <returns>True when the trace file was written completely; otherwise false.</returns>
    internal async Task<bool> WaitForTraceWriteAsync(TimeSpan timeout)
    {
        Task<bool>? traceWriteTask = _traceWriteTask;
        if (traceWriteTask is null)
        {
            return false;
        }

        // Cancel the timeout once the writer wins, so the pending delay does not keep a timer alive
        // for the full timeout on every stop.
        using CancellationTokenSource timeoutCancellation = new();
        Task delayTask = Task.Delay(timeout, timeoutCancellation.Token);
        Task completed = await Task.WhenAny(traceWriteTask, delayTask).ConfigureAwait(false);
        timeoutCancellation.Cancel();

        if (completed == traceWriteTask)
        {
            // WriteTraceAsync never faults, so the result can be observed directly.
            return await traceWriteTask.ConfigureAwait(false);
        }

        _logger.LogWarning(
            "Timed out after {timeout} waiting for the trace file to finish writing. The trace is incomplete and will not be processed.",
            timeout);
        return false;
    }

    /// <summary>
    /// Copies the EventPipe stream to the trace file.
    /// </summary>
    /// <returns>True when the whole stream was copied; otherwise false.</returns>
    private async Task<bool> WriteTraceAsync(string traceFilePath, Stream stream)
    {
        _logger.LogInformation("Start writing trace file {traceFilePath}...", traceFilePath);
        try
        {
            // FileMode.Create rather than File.OpenWrite (which is FileMode.OpenOrCreate), so a
            // pre-existing file is truncated instead of leaving a tail of stale bytes behind.
            using FileStream fileStream = new(traceFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
            await stream.CopyToAsync(fileStream).ConfigureAwait(false);
            _logger.LogInformation("Finished writing trace file {traceFilePath}.", traceFilePath);
            return true;
        }
        catch (ObjectDisposedException ex) when (_stopRequested)
        {
            // The EventPipe stream was closed while the session was being stopped or disposed - for
            // example the host tore down the DI container mid-session. The trace is incomplete, but
            // this is a lifecycle outcome rather than an application fault, so it is not an error.
            _logger.LogWarning(
                ex,
                "The EventPipe stream was closed while the profiler was stopping, so trace file {traceFilePath} is incomplete. It will not be processed.",
                traceFilePath);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error writing trace file at: {filePath}", traceFilePath);
            return false;
        }
    }
}