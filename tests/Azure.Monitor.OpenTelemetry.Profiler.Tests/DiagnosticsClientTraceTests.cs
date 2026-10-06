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
/// Session-lifecycle tests for issue #191. These drive a real EventPipe session against the test
/// process, which is how the profiler runs in production (it profiles its own process), so the
/// stop -> drain -> dispose ordering is exercised end to end rather than simulated.
/// </summary>
public class DiagnosticsClientTraceTests : IDisposable
{
    // The .nettrace format starts with this signature. A complete file must contain at least it.
    private static readonly byte[] NetTraceSignature = "Nettrace"u8.ToArray();

    private readonly string _traceFilePath = Path.Combine(
        Path.GetTempPath(), $"{Guid.NewGuid()}.nettrace");

    [Fact]
    public async Task EnableThenDisable_WritesACompleteTraceFileBeforeReportingSuccess()
    {
        // The regression: DisableAsync used to return while the writer was still draining, so the
        // caller handed a truncated (sometimes zero-length) file to the uploader.
        CapturingLogger logger = new();
        using DiagnosticsClientTrace target = CreateTarget(logger);

        await target.EnableAsync(_traceFilePath, CancellationToken.None);
        Assert.True(await target.DisableAsync(CancellationToken.None));

        // The file must already be complete and closed by the time DisableAsync returns.
        byte[] content = await File.ReadAllBytesAsync(_traceFilePath);
        Assert.True(content.Length > NetTraceSignature.Length, $"Trace file was {content.Length} bytes.");
        Assert.Equal(NetTraceSignature, content.Take(NetTraceSignature.Length));
        Assert.DoesNotContain(logger.Snapshot(), e => e.Level >= LogLevel.Error);
    }

    [Fact]
    public async Task Dispose_WithALiveSession_StopsTheSessionWithoutLoggingAnError()
    {
        // The reported crash: the DI container disposed the singleton mid-session, closing the pipe
        // under the writer. Disposal must stop the session so the writer can finish, and must never
        // surface the resulting closed pipe as an application error.
        CapturingLogger logger = new();
        DiagnosticsClientTrace target = CreateTarget(logger);

        await target.EnableAsync(_traceFilePath, CancellationToken.None);
        target.Dispose();

        Assert.DoesNotContain(logger.Snapshot(), e => e.Level >= LogLevel.Error);

        // Without stopping the session first, the stream never reaches EOF, so the drain would run
        // out its full budget and report a timeout before truncating the trace anyway.
        Assert.DoesNotContain(logger.Snapshot(), e => e.Message.Contains("Timed out"));
    }

    [Fact]
    public async Task EnableAsync_WhenASessionIsAlreadyRunning_Throws()
    {
        // Previously this silently overwrote the session field, leaking the old EventPipe session
        // and its pipe handle.
        using DiagnosticsClientTrace target = CreateTarget();
        await target.EnableAsync(_traceFilePath, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => target.EnableAsync(_traceFilePath, CancellationToken.None));

        await target.DisableAsync(CancellationToken.None);
    }

    [Fact]
    public async Task EnableAsync_AfterDisable_StartsAFreshSession()
    {
        using DiagnosticsClientTrace target = CreateTarget();

        await target.EnableAsync(_traceFilePath, CancellationToken.None);
        Assert.True(await target.DisableAsync(CancellationToken.None));

        await target.EnableAsync(_traceFilePath, CancellationToken.None);
        Assert.True(await target.DisableAsync(CancellationToken.None));
    }

    [Fact]
    public async Task EnableAsync_AfterDispose_Throws()
    {
        DiagnosticsClientTrace target = CreateTarget();
        target.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => target.EnableAsync(_traceFilePath, CancellationToken.None));
    }

    [Fact]
    public async Task DisableAsync_WhenNoSessionExists_ReportsIncomplete()
    {
        using DiagnosticsClientTrace target = CreateTarget();

        Assert.False(await target.DisableAsync(CancellationToken.None));
    }

    [Fact]
    public async Task DisableAsync_Twice_ReportsIncompleteTheSecondTime()
    {
        using DiagnosticsClientTrace target = CreateTarget();
        await target.EnableAsync(_traceFilePath, CancellationToken.None);

        Assert.True(await target.DisableAsync(CancellationToken.None));
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
