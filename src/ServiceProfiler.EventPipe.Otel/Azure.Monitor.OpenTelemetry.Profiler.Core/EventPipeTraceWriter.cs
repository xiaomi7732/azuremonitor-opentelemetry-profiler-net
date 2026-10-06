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
    /// The "Nettrace" magic every trace file starts with, followed by a length-prefixed serializer
    /// signature when the FastSerialization framing (format versions 4 and 5) is in use.
    /// </summary>
    private static readonly byte[] NetTraceMagic = "Nettrace"u8.ToArray();
    private static readonly byte[] FastSerializationSignature = "!FastSerialization.1"u8.ToArray();

    /// <summary>
    /// The trailer of a complete nettrace stream in the FastSerialization framing used by format
    /// versions 4 and 5: the final object is closed with an EndObject tag and the stream is then
    /// terminated with a NullReference tag.
    /// </summary>
    private static readonly byte[] NetTraceV5Trailer = [6 /* EndObject */, 1 /* NullReference */];

    /// <summary>
    /// The trailer of a complete nettrace stream in format version 6, which drops the
    /// FastSerialization framing for a sequence of blocks terminated by an empty EndOfStream block
    /// (a 4-byte header of 24-bit size 0 and block kind 0).
    /// </summary>
    private static readonly byte[] NetTraceV6Trailer = [0, 0, 0, 0 /* EndOfStream block */];

    /// <summary>
    /// The highest nettrace format version whose terminator this profiler knows how to verify.
    /// </summary>
    private const uint HighestVerifiableMajorVersion = 6;

    /// <summary>
    /// Whether the written file ends with the nettrace stream terminator for its own framing.
    /// <para>
    /// The framing is taken from the header rather than accepting any terminator, so a version 4/5
    /// trace cut short on four zero bytes is still rejected.
    /// </para>
    /// <para>
    /// A version this profiler does not know is allowed through instead of being rejected. This
    /// gate decides whether a trace is uploaded at all, so the two failure directions are not
    /// symmetric: wrongly accepting means occasionally uploading a short trace, which is what
    /// happened before this check existed, whereas wrongly rejecting means silently uploading
    /// nothing at all, for every session, while the profiler still reports healthy stops. Unknown
    /// formats therefore fall back to the stop-ordering check alone.
    /// </para>
    /// </summary>
    private bool EndsWithNetTraceTrailer(FileStream fileStream, string traceFilePath)
    {
        // Anything that is not a nettrace stream at all cannot be a complete one - this also rules
        // out a file so short that the framing probe below would have nothing to read.
        if (!StartsWith(fileStream, NetTraceMagic))
        {
            return false;
        }

        if (UsesFastSerializationFraming(fileStream))
        {
            return EndsWith(fileStream, NetTraceV5Trailer);
        }

        if (TryReadMajorVersion(fileStream, out uint majorVersion) && majorVersion > HighestVerifiableMajorVersion)
        {
            _logger.LogDebug(
                "Trace file {traceFilePath} uses nettrace format version {majorVersion}, whose terminator this profiler cannot verify. Skipping the trailer check rather than discarding the trace.",
                traceFilePath,
                majorVersion);
            return true;
        }

        return EndsWith(fileStream, NetTraceV6Trailer);
    }

    /// <summary>
    /// Reads the major version from a post-FastSerialization nettrace header: the magic, a reserved
    /// field, then the version.
    /// </summary>
    private static bool TryReadMajorVersion(FileStream fileStream, out uint majorVersion)
    {
        const int majorVersionOffset = 8 + 4;

        majorVersion = 0;
        if (fileStream.Length < majorVersionOffset + sizeof(uint))
        {
            return false;
        }

        fileStream.Seek(majorVersionOffset, SeekOrigin.Begin);

        byte[] value = new byte[sizeof(uint)];
        for (int i = 0; i < value.Length; i++)
        {
            int read = fileStream.ReadByte();
            if (read < 0)
            {
                return false;
            }

            value[i] = (byte)read;
        }

        majorVersion = BitConverter.ToUInt32(value, 0);
        return true;
    }

    private static bool UsesFastSerializationFraming(FileStream fileStream)
    {
        // "Nettrace", then a 4-byte length, then the serializer signature.
        const int signatureOffset = 8 + 4;

        if (fileStream.Length < signatureOffset + FastSerializationSignature.Length)
        {
            return false;
        }

        fileStream.Seek(signatureOffset, SeekOrigin.Begin);
        return Matches(fileStream, FastSerializationSignature);
    }

    private static bool StartsWith(FileStream fileStream, byte[] prefix)
    {
        if (fileStream.Length < prefix.Length)
        {
            return false;
        }

        fileStream.Seek(0, SeekOrigin.Begin);
        return Matches(fileStream, prefix);
    }

    private static bool EndsWith(FileStream fileStream, byte[] trailer)
    {
        if (fileStream.Length < trailer.Length)
        {
            return false;
        }

        fileStream.Seek(-trailer.Length, SeekOrigin.End);
        return Matches(fileStream, trailer);
    }

    private static bool Matches(FileStream fileStream, byte[] expected)
    {
        foreach (byte b in expected)
        {
            if (fileStream.ReadByte() != b)
            {
                return false;
            }
        }

        return true;
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
            // must carry the nettrace trailer, which only a fully delivered trace (including
            // rundown) has.
            if (!_stopRequested)
            {
                _logger.LogWarning(
                    "The EventPipe stream ended before the profiler asked the session to stop, so trace file {traceFilePath} is incomplete. It will not be processed.",
                    traceFilePath);
                return false;
            }

            if (!EndsWithNetTraceTrailer(fileStream, traceFilePath))
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
