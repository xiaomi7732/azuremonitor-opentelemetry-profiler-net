//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using Azure.Monitor.OpenTelemetry.Profiler.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Azure.Monitor.OpenTelemetry.Profiler.Tests;

/// <summary>
/// Regression tests for issue #191: the trace writer used to be fire-and-forget, so stopping or
/// disposing the EventPipe session could close the stream underneath it. That truncated the trace
/// file - which was then uploaded unvalidated - and surfaced a closed-pipe
/// <see cref="ObjectDisposedException"/> as an application error.
/// </summary>
public class EventPipeTraceWriterTests : IDisposable
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    // The trailer of a complete nettrace stream (format v4/v5): EndObject then NullReference.
    private static readonly byte[] NetTraceTrailer = [6, 1];

    // The format v6 terminator: an empty EndOfStream block header.
    private static readonly byte[] NetTraceV6Trailer = [0, 0, 0, 0];

    private static readonly byte[] NetTraceMagic = "Nettrace"u8.ToArray();
    private static readonly byte[] FastSerializationSignature = "!FastSerialization.1"u8.ToArray();

    private readonly string _traceFilePath = Path.Combine(
        Path.GetTempPath(), $"{Guid.NewGuid()}.nettrace");

    [Fact]
    public async Task WaitAsync_WhenTheWholeStreamIsCopied_ReportsCompleteAndWritesEveryByte()
    {
        EventPipeTraceWriter target = new(NullLogger.Instance);
        byte[] payload = CreatePayload(64 * 1024);

        // A stop must already be in flight for an end-of-stream to mean "complete".
        target.RequestStop();
        target.Start(_traceFilePath, new MemoryStream(payload));

        Assert.True(await target.WaitAsync(TestTimeout));
        Assert.Equal(payload, await File.ReadAllBytesAsync(_traceFilePath));
    }

    [Fact]
    public async Task WaitAsync_DoesNotReturnUntilTheWriterHasDrainedTheStream()
    {
        // The core of #191: the stop path must not report the trace as usable while bytes are still
        // in flight, otherwise the uploader reads a truncated file.
        EventPipeTraceWriter target = new(NullLogger.Instance);
        byte[] payload = CreatePayload(32 * 1024);
        using BlockingStream stream = new(payload);

        target.Start(_traceFilePath, stream);
        await WithTimeout(stream.FirstReadStarted);
        target.RequestStop();

        Task<bool> waitTask = target.WaitAsync(TestTimeout);
        Assert.False(waitTask.IsCompleted);

        stream.ReleaseRemainder();

        Assert.True(await waitTask);
        Assert.Equal(payload, await File.ReadAllBytesAsync(_traceFilePath));
    }

    [Fact]
    public async Task WaitAsync_WhenTheWriterExceedsTheTimeout_ReportsIncomplete()
    {
        EventPipeTraceWriter target = new(NullLogger.Instance);
        using BlockingStream stream = new(CreatePayload(32 * 1024));

        target.Start(_traceFilePath, stream);
        await WithTimeout(stream.FirstReadStarted);

        Assert.False(await target.WaitAsync(TimeSpan.FromMilliseconds(50)));

        // Let the writer finish before the file is cleaned up, so it is not still holding it open.
        stream.ReleaseRemainder();
        await target.WaitAsync(TestTimeout);
    }

    [Fact]
    public async Task WaitAsync_WhenTheWriterWasNeverStarted_ReportsIncomplete()
    {
        EventPipeTraceWriter target = new(NullLogger.Instance);

        Assert.False(await target.WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task Writer_WhenTheStreamIsClosedWhileStopping_ReportsIncompleteWithoutLoggingAnError()
    {
        // The reported symptom: the EventPipe stream is closed while the writer is still copying.
        // Once a stop has been requested that is a lifecycle outcome, not an application fault.
        CapturingLogger logger = new();
        EventPipeTraceWriter target = new(logger);
        BlockingStream stream = new(CreatePayload(32 * 1024));

        target.Start(_traceFilePath, stream);
        await WithTimeout(stream.FirstReadStarted);

        target.RequestStop();
        stream.Dispose();

        Assert.False(await target.WaitAsync(TestTimeout));
        Assert.DoesNotContain(logger.Snapshot(), e => e.Level >= LogLevel.Error);
        Assert.Contains(logger.Snapshot(), e => e.Level == LogLevel.Warning && e.Message.Contains("incomplete"));
    }

    [Fact]
    public async Task Writer_WhenTheStreamFailsOutsideAStop_LogsAnErrorAndReportsIncomplete()
    {
        // Outside of a stop, a closed stream is a genuine failure and must stay visible.
        CapturingLogger logger = new();
        EventPipeTraceWriter target = new(logger);
        BlockingStream stream = new(CreatePayload(32 * 1024));

        target.Start(_traceFilePath, stream);
        await WithTimeout(stream.FirstReadStarted);

        stream.Dispose();

        Assert.False(await target.WaitAsync(TestTimeout));
        Assert.Contains(logger.Snapshot(), e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task Writer_KeepsItsOwnStoppingState_SoAnAbandonedWriterIsStillTreatedAsStopping()
    {
        // A writer abandoned on the timeout path must not be re-classified as an error by whatever
        // the next session does; the stopping state belongs to the writer, not to the trace control.
        CapturingLogger logger = new();
        EventPipeTraceWriter abandoned = new(logger);
        BlockingStream stream = new(CreatePayload(32 * 1024));

        abandoned.Start(_traceFilePath, stream);
        await WithTimeout(stream.FirstReadStarted);

        abandoned.RequestStop();
        Assert.False(await abandoned.WaitAsync(TimeSpan.FromMilliseconds(50)));

        // A brand new writer for the next session cannot clear the abandoned writer's state.
        EventPipeTraceWriter next = new(logger);
        Assert.False(next.StopRequested);
        Assert.True(abandoned.StopRequested);

        // The abandoned writer now observes its stream being closed, as disposal of the old session
        // would do, and still classifies it as an expected stop.
        stream.Dispose();
        Assert.False(await abandoned.WaitAsync(TestTimeout));
        Assert.DoesNotContain(logger.Snapshot(), e => e.Level >= LogLevel.Error);
    }

    [Fact]
    public async Task Writer_TruncatesAnExistingFile()
    {
        // File.OpenWrite (FileMode.OpenOrCreate) would leave a tail of stale bytes behind.
        await File.WriteAllBytesAsync(_traceFilePath, CreatePayload(128 * 1024));

        EventPipeTraceWriter target = new(NullLogger.Instance);
        byte[] payload = CreatePayload(1024);

        target.RequestStop();
        target.Start(_traceFilePath, new MemoryStream(payload));

        Assert.True(await target.WaitAsync(TestTimeout));
        Assert.Equal(payload, await File.ReadAllBytesAsync(_traceFilePath));
    }

    [Fact]
    public async Task Writer_WhenTheStreamEndsBeforeAStopWasRequested_ReportsIncomplete()
    {
        // The peer closing the diagnostics pipe is a clean end-of-stream, not an exception. A
        // runtime that goes away mid-session therefore yields a short file and no error at all, so
        // reaching EOF cannot by itself mean the trace is complete.
        CapturingLogger logger = new();
        EventPipeTraceWriter target = new(logger);

        target.Start(_traceFilePath, new MemoryStream(CreatePayload(4096)));

        Assert.False(await target.WaitAsync(TestTimeout));
        Assert.Contains(logger.Snapshot(), e => e.Level == LogLevel.Warning && e.Message.Contains("ended before"));
    }

    [Fact]
    public async Task Writer_WhenNoDataWasWritten_ReportsIncomplete()
    {
        EventPipeTraceWriter target = new(NullLogger.Instance);

        target.RequestStop();
        target.Start(_traceFilePath, new MemoryStream(Array.Empty<byte>()));

        Assert.False(await target.WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task Writer_WhenTheTraceIsMissingItsEndOfStreamMarker_ReportsIncomplete()
    {
        // A runtime that disconnects during rundown still produces a clean end-of-stream after the
        // stop was requested, so only the nettrace terminator distinguishes it from a full trace.
        CapturingLogger logger = new();
        EventPipeTraceWriter target = new(logger);

        target.RequestStop();
        target.Start(_traceFilePath, new MemoryStream(CreateTruncatedPayload(4096)));

        Assert.False(await target.WaitAsync(TestTimeout));
        Assert.Contains(logger.Snapshot(), e => e.Level == LogLevel.Warning && e.Message.Contains("end-of-stream marker"));
    }

    [Fact]
    public async Task Writer_WhenTheTraceUsesTheNewerFormatTerminator_ReportsComplete()
    {
        // The gate decides whether a trace is uploaded at all, so it must not fail closed against
        // the format version 6 terminator - that would silently stop every upload while the
        // profiler still looked healthy.
        EventPipeTraceWriter target = new(NullLogger.Instance);
        byte[] payload = CreateV6Payload(4096);

        target.RequestStop();
        target.Start(_traceFilePath, new MemoryStream(payload));

        Assert.True(await target.WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task Writer_WhenAFastSerializationTraceEndsInTheNewerTerminator_ReportsIncomplete()
    {
        // Accepting either terminator regardless of framing would let a version 4/5 trace that was
        // cut off on four zero bytes pass as complete. The framing is taken from the header.
        CapturingLogger logger = new();
        EventPipeTraceWriter target = new(logger);

        byte[] payload = CreatePayload(4096);
        Array.Clear(payload, payload.Length - 4, 4);

        target.RequestStop();
        target.Start(_traceFilePath, new MemoryStream(payload));

        Assert.False(await target.WaitAsync(TestTimeout));
        Assert.Contains(logger.Snapshot(), e => e.Level == LogLevel.Warning && e.Message.Contains("end-of-stream marker"));
    }

    [Fact]
    public async Task Writer_WhenTheFileIsNotANetTraceStream_ReportsIncomplete()
    {
        // Without requiring the magic, anything that merely ended in four zero bytes - including a
        // four-byte file of zeros - would be treated as a complete v6 trace.
        EventPipeTraceWriter target = new(NullLogger.Instance);

        target.RequestStop();
        target.Start(_traceFilePath, new MemoryStream(new byte[4]));

        Assert.False(await target.WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task Writer_WhenTheTraceUsesAFormatVersionItCannotVerify_ReportsComplete()
    {
        // The gate must degrade to permissive on a format it does not know. Rejecting would mean
        // uploading nothing at all, silently, for every session - far worse than occasionally
        // accepting a short trace, which is simply what happened before this check existed.
        EventPipeTraceWriter target = new(NullLogger.Instance);
        byte[] payload = CreateFutureVersionPayload(4096);

        target.RequestStop();
        target.Start(_traceFilePath, new MemoryStream(payload));

        Assert.True(await target.WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task Writer_WhenAKnownFormatVersionIsTruncated_StillReportsIncomplete()
    {
        // Degrading on unknown versions must not weaken the check for the versions it does know.
        CapturingLogger logger = new();
        EventPipeTraceWriter target = new(logger);

        byte[] payload = CreateV6Payload(4096);
        payload[^1] = 0xFF;

        target.RequestStop();
        target.Start(_traceFilePath, new MemoryStream(payload));

        Assert.False(await target.WaitAsync(TestTimeout));
        Assert.Contains(logger.Snapshot(), e => e.Level == LogLevel.Warning && e.Message.Contains("end-of-stream marker"));
    }

    [Fact]
    public void Start_WhenAlreadyStarted_Throws()    {
        EventPipeTraceWriter target = new(NullLogger.Instance);
        target.Start(_traceFilePath, new MemoryStream(CreatePayload(64)));

        Assert.Throws<InvalidOperationException>(() => target.Start(_traceFilePath, new MemoryStream()));
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try
        {
            File.Delete(_traceFilePath);
        }
        catch (IOException)
        {
        }
    }

    private static async Task WithTimeout(Task task)
    {
        Assert.Same(task, await Task.WhenAny(task, Task.Delay(TestTimeout)));
        await task;
    }

    /// <summary>
    /// A payload shaped like a complete nettrace in the FastSerialization framing (format v4/v5):
    /// the "Nettrace" magic and serializer signature up front, the stream trailer at the end.
    /// </summary>
    private static byte[] CreatePayload(int length)
    {
        byte[] payload = new byte[length];
        new Random(Seed: length).NextBytes(payload);
        WriteFastSerializationHeader(payload);
        NetTraceTrailer.CopyTo(payload, length - NetTraceTrailer.Length);
        return payload;
    }

    /// <summary>
    /// A payload shaped like a complete nettrace in format v6: no FastSerialization signature, a
    /// version header, and an empty EndOfStream block at the end.
    /// </summary>
    private static byte[] CreateV6Payload(int length)
    {
        byte[] payload = new byte[length];
        new Random(Seed: length).NextBytes(payload);
        WriteVersionHeader(payload, majorVersion: 6);
        NetTraceV6Trailer.CopyTo(payload, length - NetTraceV6Trailer.Length);
        return payload;
    }

    /// <summary>
    /// A payload declaring a format version newer than anything this profiler knows how to verify,
    /// with a tail that matches no known terminator.
    /// </summary>
    private static byte[] CreateFutureVersionPayload(int length)
    {
        byte[] payload = new byte[length];
        new Random(Seed: length).NextBytes(payload);
        WriteVersionHeader(payload, majorVersion: 99);
        payload[^1] = 0xFF;
        return payload;
    }

    /// <summary>
    /// Writes the post-FastSerialization header: magic, reserved, major version, minor version.
    /// </summary>
    private static void WriteVersionHeader(byte[] payload, uint majorVersion)
    {
        NetTraceMagic.CopyTo(payload, 0);
        BitConverter.GetBytes(0u).CopyTo(payload, 8);
        BitConverter.GetBytes(majorVersion).CopyTo(payload, 12);
        BitConverter.GetBytes(0u).CopyTo(payload, 16);
    }

    /// <summary>
    /// A payload that stops short, as a trace cut off mid-stream does. It deliberately ends with the
    /// last byte of the trailer, so a check that only looked at the final byte would wrongly pass.
    /// </summary>
    private static byte[] CreateTruncatedPayload(int length)
    {
        byte[] payload = new byte[length];
        new Random(Seed: length).NextBytes(payload);
        WriteFastSerializationHeader(payload);
        payload[length - 2] = 0xFF;
        payload[length - 1] = NetTraceTrailer[^1];
        return payload;
    }

    private static void WriteFastSerializationHeader(byte[] payload)
    {
        NetTraceMagic.CopyTo(payload, 0);
        BitConverter.GetBytes(FastSerializationSignature.Length).CopyTo(payload, NetTraceMagic.Length);
        FastSerializationSignature.CopyTo(payload, NetTraceMagic.Length + 4);
    }

    /// <summary>
    /// A stream that serves its first byte, then blocks until the test releases it or disposes it.
    /// This keeps the writer demonstrably mid-copy so stop/dispose ordering can be exercised
    /// deterministically, and disposal models a closed pipe.
    /// </summary>
    private sealed class BlockingStream : Stream
    {
        private readonly MemoryStream _inner;
        private readonly TaskCompletionSource _firstReadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _firstReadServed;
        private volatile bool _disposed;

        public BlockingStream(byte[] content) => _inner = new MemoryStream(content);

        public Task FirstReadStarted => _firstReadStarted.Task;

        public void ReleaseRemainder() => _release.TrySetResult();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_firstReadServed)
            {
                _firstReadServed = true;
                int served = await _inner.ReadAsync(buffer[..1], cancellationToken).ConfigureAwait(false);
                _firstReadStarted.TrySetResult();
                return served;
            }

            await _release.Task.ConfigureAwait(false);

            if (_disposed)
            {
                // Mirrors PipeStream.CheckReadOperations once the pipe has been closed locally.
                throw new ObjectDisposedException(nameof(BlockingStream), "Cannot access a closed pipe.");
            }

            return await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        }

        public override int Read(byte[] buffer, int offset, int count)
            => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        protected override void Dispose(bool disposing)
        {
            _disposed = true;
            // Unblock any pending read so it observes the disposal, as a closed pipe would.
            _release.TrySetResult();
            base.Dispose(disposing);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class CapturingLogger : ILogger
    {
        private readonly object _gate = new();
        private readonly List<(LogLevel Level, string Message)> _entries = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public IReadOnlyList<(LogLevel Level, string Message)> Snapshot()
        {
            lock (_gate)
            {
                return _entries.ToList();
            }
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_gate)
            {
                _entries.Add((logLevel, formatter(state, exception)));
            }
        }
    }
}
