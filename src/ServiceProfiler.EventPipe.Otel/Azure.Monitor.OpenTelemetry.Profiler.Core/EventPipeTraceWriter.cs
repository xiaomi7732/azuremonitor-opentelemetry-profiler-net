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
    /// Waits for the copy to finish within <paramref name="timeout"/>, or until
    /// <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    /// <returns>
    /// True when the whole stream was copied, so the trace file is complete and safe to process;
    /// otherwise false.
    /// </returns>
    public async Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        Task<bool>? completion = _completion;
        if (completion is null)
        {
            return false;
        }

        if (!completion.IsCompleted)
        {
            // Cancel the timeout once the writer wins, so a pending delay does not keep a timer
            // alive for the full timeout on every stop. The caller's token is observed too: the
            // drain budget is generous enough to cover rundown, which is far longer than a host is
            // willing to wait once it has started shutting down.
            using CancellationTokenSource waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Task delayTask = Task.Delay(timeout, waitCancellation.Token);
            Task completed = await Task.WhenAny(completion, delayTask).ConfigureAwait(false);
            waitCancellation.Cancel();

            if (completed != completion)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogDebug("Stopped waiting for the trace file to finish writing because the operation was cancelled. The trace is incomplete and will not be processed.");
                }
                else
                {
                    _logger.LogWarning(
                        "Timed out after {timeout} waiting for the trace file to finish writing. The trace is incomplete and will not be processed.",
                        timeout);
                }

                return false;
            }
        }

        // WriteAsync never faults, so the result can be observed directly.
        return await completion.ConfigureAwait(false);
    }

    /// <summary>
    /// The nettrace stream is terminated by a NullReference tag. A trace that was cut short - the
    /// runtime going away mid-session or mid-rundown - ends without it.
    /// </summary>
    private const byte NetTraceEndOfStreamTag = 1;

    /// <summary>
    /// Whether the written file carries the nettrace end-of-stream marker.
    /// </summary>
    private static bool EndsWithEndOfStreamTag(FileStream fileStream)
    {
        if (fileStream.Length == 0)
        {
            return false;
        }

        fileStream.Seek(-1, SeekOrigin.End);
        return fileStream.ReadByte() == NetTraceEndOfStreamTag;
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
            // Opened for read as well so the completeness check below can inspect the trailer.
            using FileStream fileStream = new(traceFilePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
            await stream.CopyToAsync(fileStream).ConfigureAwait(false);

            // Reaching the end of the stream is not by itself proof of a complete trace. The peer
            // closing the diagnostics pipe is a clean end-of-stream, not an exception, so a runtime
            // that goes away mid-session produces a short file and no error at all. Two things have
            // to hold: the stream must have ended after we asked the session to stop, and the file
            // must carry the nettrace end-of-stream marker, which only a fully delivered trace
            // (including rundown) has.
            if (!_stopRequested)
            {
                _logger.LogWarning(
                    "The EventPipe stream ended before the profiler asked the session to stop, so trace file {traceFilePath} is incomplete. It will not be processed.",
                    traceFilePath);
                return false;
            }

            if (!EndsWithEndOfStreamTag(fileStream))
            {
                _logger.LogWarning(
                    "Trace file {traceFilePath} does not end with the nettrace end-of-stream marker, so it is incomplete. It will not be processed.",
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
