//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace Azure.Monitor.OpenTelemetry.Profiler.Core;

/// <summary>
/// Copies an EventPipe stream to a trace file and lets the session lifecycle coordinate with that
/// copy.
/// <para>
/// Each writer owns its own stopping state. Closing the EventPipe stream is how a session is torn
/// down, so a copy that fails because of it is an expected lifecycle outcome rather than a fault -
/// but only for the session that is actually stopping. Keeping the state per writer means an
/// abandoned writer cannot have it reset by a later session and be misreported as an error.
/// </para>
/// </summary>
internal sealed class EventPipeTraceWriter
{
    private readonly ILogger _logger;
    private volatile bool _stopRequested;
    private volatile Task<bool>? _completion;

    public EventPipeTraceWriter(ILogger logger)
        => _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Whether the owning session has been asked to stop.
    /// </summary>
    public bool StopRequested => _stopRequested;

    /// <summary>
    /// Marks the owning session as stopping, so a closed EventPipe stream is treated as an expected
    /// outcome rather than a failure.
    /// </summary>
    public void RequestStop() => _stopRequested = true;

    /// <summary>
    /// Starts copying <paramref name="stream"/> to <paramref name="traceFilePath"/>.
    /// </summary>
    public void Start(string traceFilePath, Stream stream)
    {
        if (_completion is not null)
        {
            throw new InvalidOperationException("The trace writer has already been started.");
        }

        _completion = WriteAsync(traceFilePath, stream);
    }

    /// <summary>
    /// Waits for the copy to finish within <paramref name="timeout"/>.
    /// </summary>
    /// <returns>
    /// True when the whole stream was copied, so the trace file is complete and safe to process;
    /// otherwise false.
    /// </returns>
    public async Task<bool> WaitAsync(TimeSpan timeout)
    {
        Task<bool>? completion = _completion;
        if (completion is null)
        {
            return false;
        }

        if (!completion.IsCompleted)
        {
            // Cancel the timeout once the writer wins, so a pending delay does not keep a timer
            // alive for the full timeout on every stop.
            using CancellationTokenSource timeoutCancellation = new();
            Task delayTask = Task.Delay(timeout, timeoutCancellation.Token);
            Task completed = await Task.WhenAny(completion, delayTask).ConfigureAwait(false);
            timeoutCancellation.Cancel();

            if (completed != completion)
            {
                _logger.LogWarning(
                    "Timed out after {timeout} waiting for the trace file to finish writing. The trace is incomplete and will not be processed.",
                    timeout);
                return false;
            }
        }

        // WriteAsync never faults, so the result can be observed directly.
        return await completion.ConfigureAwait(false);
    }

    /// <summary>
    /// Whether the exception is one that closing the EventPipe stream produces.
    /// <para>
    /// The Windows transport is a named pipe, which reports a local close as
    /// <see cref="ObjectDisposedException"/>. The Unix transport is a socket, where a close under a
    /// pending read can instead surface as an <see cref="IOException"/> wrapping a
    /// <see cref="SocketException"/>. A plain <see cref="IOException"/> is deliberately not matched:
    /// the copy also writes the destination file, and a storage failure there is actionable and must
    /// stay visible as an error.
    /// </para>
    /// </summary>
    private static bool IsStreamTornDown(Exception ex) => ex switch
    {
        ObjectDisposedException => true,
        OperationCanceledException => true,
        IOException { InnerException: SocketException } => true,
        _ => false,
    };

    private async Task<bool> WriteAsync(string traceFilePath, Stream stream)
    {
        _logger.LogInformation("Start writing trace file {traceFilePath}...", traceFilePath);
        try
        {
            // FileMode.Create rather than File.OpenWrite (which is FileMode.OpenOrCreate), so a
            // pre-existing file is truncated instead of leaving a tail of stale bytes behind.
            using FileStream fileStream = new(traceFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
            await stream.CopyToAsync(fileStream).ConfigureAwait(false);

            // Reaching the end of the stream is not by itself proof of a complete trace. The peer
            // closing the diagnostics pipe is a clean end-of-stream, not an exception, so a runtime
            // that goes away mid-session produces a short file and no error at all. Only a stream
            // that ended after we asked the session to stop has actually delivered everything,
            // including rundown.
            if (!_stopRequested)
            {
                _logger.LogWarning(
                    "The EventPipe stream ended before the profiler asked the session to stop, so trace file {traceFilePath} is incomplete. It will not be processed.",
                    traceFilePath);
                return false;
            }

            if (fileStream.Length == 0)
            {
                _logger.LogWarning(
                    "No trace data was written to {traceFilePath}. It will not be processed.",
                    traceFilePath);
                return false;
            }

            _logger.LogInformation("Finished writing trace file {traceFilePath}.", traceFilePath);
            return true;
        }
        catch (Exception ex) when (_stopRequested && IsStreamTornDown(ex))
        {
            // The EventPipe stream was torn down while this session was being stopped or disposed -
            // for example the host disposed the DI container mid-session. The trace is incomplete,
            // but this is a lifecycle outcome rather than an application fault, so it is not an
            // error.
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
