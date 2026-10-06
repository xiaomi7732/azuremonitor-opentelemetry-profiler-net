//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

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

    private async Task<bool> WriteAsync(string traceFilePath, Stream stream)
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
        catch (Exception ex) when (_stopRequested && ex is ObjectDisposedException or IOException or OperationCanceledException)
        {
            // The EventPipe stream was torn down while this session was being stopped or disposed -
            // for example the host disposed the DI container mid-session. On Windows that surfaces
            // as a closed pipe; on Unix a severed socket can surface as an IOException instead. The
            // trace is incomplete, but this is a lifecycle outcome rather than an application fault,
            // so it is not an error.
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
