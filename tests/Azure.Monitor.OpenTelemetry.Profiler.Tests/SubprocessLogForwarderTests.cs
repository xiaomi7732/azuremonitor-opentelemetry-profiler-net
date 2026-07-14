// -----------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
// -----------------------------------------------------------------------------

using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.UploaderProxy;
using Microsoft.Extensions.Logging;

namespace Azure.Monitor.OpenTelemetry.Profiler.Tests;

public class SubprocessLogForwarderTests
{
    [Theory]
    [InlineData("crit:", LogLevel.Critical)]
    [InlineData("fail:", LogLevel.Error)]
    [InlineData("warn:", LogLevel.Warning)]
    [InlineData("info:", LogLevel.Information)]
    [InlineData("dbug:", LogLevel.Debug)]
    [InlineData("trce:", LogLevel.Trace)]
    public void ParseLogLevel_RecognizedPrefix_ReturnsCorrectLevel(string prefix, LogLevel expected)
    {
        string line = $"{prefix} Some.Namespace[0] A log message";
        LogLevel result = SubprocessLogForwarder.ParseLogLevel(line.AsSpan());
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("some random text without prefix")]
    [InlineData("INFO: uppercase is not recognized")]
    public void ParseLogLevel_UnrecognizedOrEmpty_ReturnsNone(string line)
    {
        LogLevel result = SubprocessLogForwarder.ParseLogLevel(line.AsSpan());
        Assert.Equal(LogLevel.None, result);
    }

    [Fact]
    public void Forward_EmptyOrWhitespace_DoesNotLog()
    {
        var logger = new FakeLogger();
        var sink = new FakeSink();
        var forwarder = new SubprocessLogForwarder(logger, sink);

        forwarder.Forward("");
        forwarder.Forward("   ");
        forwarder.Forward(null!);

        Assert.Empty(logger.Entries);
        Assert.Empty(sink.Entries);
    }

    [Fact]
    public void Forward_SingleLine_LogsAtCorrectLevel()
    {
        var logger = new FakeLogger();
        var sink = new FakeSink();
        var forwarder = new SubprocessLogForwarder(logger, sink);

        forwarder.Forward("warn: MyApp.Service[0] Something is wrong");

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, logger.Entries[0].Level);
        Assert.Contains("Something is wrong", logger.Entries[0].Message);
    }

    [Fact]
    public void Forward_MultipleLines_EachAtCorrectLevel()
    {
        var logger = new FakeLogger();
        var sink = new FakeSink();
        var forwarder = new SubprocessLogForwarder(logger, sink);

        string output = string.Join(Environment.NewLine, new[]
        {
            "info: MyApp.Upload[0] Upload started",
            "warn: MyApp.Upload[0] Retrying upload",
            "fail: MyApp.Upload[0] Upload failed",
        });

        forwarder.Forward(output);

        Assert.Equal(3, logger.Entries.Count);
        Assert.Equal(LogLevel.Information, logger.Entries[0].Level);     // info → Information
        Assert.Equal(LogLevel.Warning, logger.Entries[1].Level);
        Assert.Equal(LogLevel.Error, logger.Entries[2].Level);
    }

    [Fact]
    public void Forward_ContinuationLines_InheritPreviousLevel()
    {
        var logger = new FakeLogger();
        var sink = new FakeSink();
        var forwarder = new SubprocessLogForwarder(logger, sink);

        string output = string.Join(Environment.NewLine, new[]
        {
            "fail: MyApp.Upload[0] Something failed",
            "      System.Exception: Details here",
            "         at MyApp.Upload.Method()",
        });

        forwarder.Forward(output);

        Assert.Equal(3, logger.Entries.Count);
        // Continuation lines inherit the previous level (Error)
        Assert.All(logger.Entries, e => Assert.Equal(LogLevel.Error, e.Level));
    }

    [Fact]
    public void Forward_SkipsBlankLines()
    {
        var logger = new FakeLogger();
        var sink = new FakeSink();
        var forwarder = new SubprocessLogForwarder(logger, sink);

        string output = "info: MyApp[0] First\n\n\nwarn: MyApp[0] Second";

        forwarder.Forward(output);

        Assert.Equal(2, logger.Entries.Count);
    }

    [Fact]
    public void Forward_InformationAndAbove_AreForwardedToCustomerSink()
    {
        var logger = new FakeLogger();
        var sink = new FakeSink();
        var forwarder = new SubprocessLogForwarder(logger, sink);

        string output = string.Join(Environment.NewLine, new[]
        {
            "trce: MyApp.Upload[0] Trace line",
            "dbug: MyApp.Upload[0] Debug line",
            "info: MyApp.Upload[0] Upload started",
            "warn: MyApp.Upload[0] Retrying upload",
            "fail: MyApp.Upload[0] Upload failed",
            "crit: MyApp.Upload[0] Fatal",
        });

        forwarder.Forward(output);

        // All lines still go to the local logger.
        Assert.Equal(6, logger.Entries.Count);

        // Only Information and above are forwarded to the customer's Application Insights.
        Assert.Equal(4, sink.Entries.Count);
        Assert.Equal(LogLevel.Information, sink.Entries[0].Level);
        Assert.Equal(LogLevel.Warning, sink.Entries[1].Level);
        Assert.Equal(LogLevel.Error, sink.Entries[2].Level);
        Assert.Equal(LogLevel.Critical, sink.Entries[3].Level);
        Assert.All(sink.Entries, e => Assert.Contains("[Uploader]", e.Message));
    }

    [Fact]
    public void Forward_DebugAndTrace_AreNotForwardedToCustomerSink()
    {
        var logger = new FakeLogger();
        var sink = new FakeSink();
        var forwarder = new SubprocessLogForwarder(logger, sink);

        string output = string.Join(Environment.NewLine, new[]
        {
            "trce: MyApp.Upload[0] Trace line",
            "dbug: MyApp.Upload[0] Debug line",
        });

        forwarder.Forward(output);

        Assert.Equal(2, logger.Entries.Count);
        Assert.Empty(sink.Entries);
    }

    [Fact]
    public void Forward_UnprefixedLines_DefaultToDebug_AreNotForwardedToCustomerSink()
    {
        var logger = new FakeLogger();
        var sink = new FakeSink();
        var forwarder = new SubprocessLogForwarder(logger, sink);

        // No recognized prefix: default level is Debug, so nothing is forwarded to the sink.
        forwarder.Forward("some plain uploader output without a level prefix");

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Debug, logger.Entries[0].Level);
        Assert.Empty(sink.Entries);
    }

    /// <summary>
    /// Simple in-memory logger for testing.
    /// </summary>
    private sealed class FakeLogger : ILogger<SubprocessLogForwarder>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }

    /// <summary>
    /// Simple in-memory customer-AI sink for testing.
    /// </summary>
    private sealed class FakeSink : IUploaderLogForwarderSink
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public void Track(LogLevel level, string message)
        {
            Entries.Add((level, message));
        }
    }
}
