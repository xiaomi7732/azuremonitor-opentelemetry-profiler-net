//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using Azure.Monitor.OpenTelemetry.Profiler.Core;
using Microsoft.ApplicationInsights.Profiler.Shared.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Azure.Monitor.OpenTelemetry.Profiler.Tests;

/// <summary>
/// Regression tests for issue #191: the trace writer used to be fire-and-forget, so stopping or
/// disposing the EventPipe session could close the stream underneath it. That truncated the trace
/// file and surfaced a closed-pipe <see cref="ObjectDisposedException"/> as an application error.
/// </summary>
public class DiagnosticsClientTraceTests : IDisposable
{
    private readonly string _traceFilePath = Path.Combine(
        Path.GetTempPath(), $"{Guid.NewGuid()}.nettrace");

    [Fact]
    public async Task WaitForTraceWriteAsync_WhenWriterCompletes_WritesWholeStreamAndReportsComplete()
    {
        using DiagnosticsClientTrace target = CreateTarget();
        byte[] payload = CreatePayload(64 * 1024);

        target.BeginTraceWrite(_traceFilePath, new MemoryStream(payload));

        Assert.True(await target.WaitForTraceWriteAsync(TimeSpan.FromSeconds(30)));
        Assert.Equal(payload, await File.ReadAllBytesAsync(_traceFilePath));
    }

    [Fact]
    public async Task WaitForTraceWriteAsync_DoesNotReturnUntilTheWriterHasDrainedTheStream()
    {
        // The core of #191: the stop path must not report the trace as usable while bytes are still
        // in flight, otherwise the uploader reads a truncated file.
        using DiagnosticsClientTrace target = CreateTarget();
        byte[] payload = CreatePayload(32 * 1024);
        using BlockingStream stream = new(payload);

        target.BeginTraceWrite(_traceFilePath, stream);
        await stream.FirstReadStarted;

        Task<bool> waitTask = target.WaitForTraceWriteAsync(TimeSpan.FromSeconds(30));
        Assert.False(waitTask.IsCompleted);

        stream.ReleaseRemainder();

        Assert.True(await waitTask);
        Assert.Equal(payload, await File.ReadAllBytesAsync(_traceFilePath));
    }

    [Fact]
    public async Task WaitForTraceWriteAsync_WhenWriterExceedsTimeout_ReportsIncomplete()
    {
        using DiagnosticsClientTrace target = CreateTarget();
        using BlockingStream stream = new(CreatePayload(32 * 1024));

        target.BeginTraceWrite(_traceFilePath, stream);
        await stream.FirstReadStarted;

        Assert.False(await target.WaitForTraceWriteAsync(TimeSpan.FromMilliseconds(50)));

        stream.ReleaseRemainder();
    }

    [Fact]
    public async Task WaitForTraceWriteAsync_WhenNoWriterWasStarted_ReportsIncomplete()
    {
        using DiagnosticsClientTrace target = CreateTarget();

        Assert.False(await target.WaitForTraceWriteAsync(TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public async Task TraceWriter_WhenStreamIsClosedWhileStopping_ReportsIncompleteWithoutLoggingAnError()
    {
        // The reported symptom: the EventPipe stream is closed while the writer is still copying.
        // That is a lifecycle outcome once a stop has been requested, not an application fault.
        CapturingLogger logger = new();
        using DiagnosticsClientTrace target = CreateTarget(logger);
        BlockingStream stream = new(CreatePayload(32 * 1024));

        target.BeginTraceWrite(_traceFilePath, stream);
        await stream.FirstReadStarted;

        target.RequestStop();
        stream.Dispose();

        Assert.False(await target.WaitForTraceWriteAsync(TimeSpan.FromSeconds(30)));
        Assert.DoesNotContain(logger.Snapshot(), e => e.Level >= LogLevel.Error);
        Assert.Contains(logger.Snapshot(), e => e.Level == LogLevel.Warning && e.Message.Contains("incomplete"));
    }

    [Fact]
    public async Task TraceWriter_WhenStreamFailsWithoutStopping_LogsAnErrorAndReportsIncomplete()
    {
        // Outside of a stop, a closed stream is a genuine failure and must stay visible.
        CapturingLogger logger = new();
        using DiagnosticsClientTrace target = CreateTarget(logger);
        BlockingStream stream = new(CreatePayload(32 * 1024));

        target.BeginTraceWrite(_traceFilePath, stream);
        await stream.FirstReadStarted;

        stream.Dispose();

        Assert.False(await target.WaitForTraceWriteAsync(TimeSpan.FromSeconds(30)));
        Assert.Contains(logger.Snapshot(), e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task TraceWriter_TruncatesAnExistingFile()
    {
        // File.OpenWrite (FileMode.OpenOrCreate) would leave a tail of stale bytes behind.
        await File.WriteAllBytesAsync(_traceFilePath, CreatePayload(128 * 1024));

        using DiagnosticsClientTrace target = CreateTarget();
        byte[] payload = CreatePayload(1024);

        target.BeginTraceWrite(_traceFilePath, new MemoryStream(payload));

        Assert.True(await target.WaitForTraceWriteAsync(TimeSpan.FromSeconds(30)));
        Assert.Equal(payload, await File.ReadAllBytesAsync(_traceFilePath));
    }

    [Fact]
    public async Task DisableAsync_WhenNoSessionExists_ReportsIncomplete()
    {
        using DiagnosticsClientTrace target = CreateTarget();

        Assert.False(await target.DisableAsync(CancellationToken.None));
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

    private static DiagnosticsClientTrace CreateTarget(ILogger<DiagnosticsClientTrace>? logger = null)
        => new(
            DiagnosticsClientProvider.Instance,
            new DiagnosticsClientTraceConfiguration(
                Options.Create<UserConfigurationBase>(new TestUserConfiguration()),
                NullLogger<DiagnosticsClientTraceConfigurationBase>.Instance),
            logger ?? NullLogger<DiagnosticsClientTrace>.Instance);

    private sealed class TestUserConfiguration : UserConfigurationBase
    {
    }

    private static byte[] CreatePayload(int length)
    {
        byte[] payload = new byte[length];
        new Random(Seed: length).NextBytes(payload);
        return payload;
    }

    /// <summary>
    /// A stream that serves its first chunk, then blocks until the test releases it. This keeps the
    /// trace writer in flight so stop/dispose ordering can be exercised deterministically.
    /// </summary>
    private sealed class BlockingStream : Stream
    {
        private readonly MemoryStream _inner;
        private readonly TaskCompletionSource _firstReadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _firstReadServed;

        public BlockingStream(byte[] content) => _inner = new MemoryStream(content);

        public Task FirstReadStarted => _firstReadStarted.Task;

        public void ReleaseRemainder() => _release.TrySetResult();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_firstReadServed)
            {
                _firstReadServed = true;
                // Serve a single byte so the writer is demonstrably mid-copy, then signal the test.
                int served = await _inner.ReadAsync(buffer[..1], cancellationToken).ConfigureAwait(false);
                _firstReadStarted.TrySetResult();
                return served;
            }

            await _release.Task.ConfigureAwait(false);
            ObjectDisposedException.ThrowIf(_disposed, this);
            return await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        }

        public override int Read(byte[] buffer, int offset, int count)
            => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        private volatile bool _disposed;

        protected override void Dispose(bool disposing)
        {
            _disposed = true;
            // Unblock any pending read so it observes the disposal, mirroring a closed pipe.
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

    private sealed class CapturingLogger : ILogger<DiagnosticsClientTrace>
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
