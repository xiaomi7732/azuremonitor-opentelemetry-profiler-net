// -----------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
// -----------------------------------------------------------------------------

using Microsoft.ApplicationInsights.Profiler.Shared.Services.UploaderProxy;
using Microsoft.Extensions.Logging;

namespace Azure.Monitor.OpenTelemetry.Profiler.Tests;

public class SubprocessLogForwarderTests
{
    [Theory]
    [InlineData("crit:", LogLevel.Critical)]
    [InlineData("fail:", LogLevel.Error)]
    [InlineData("warn:", LogLevel.Warning)]
    [InlineData("info:", LogLevel.Debug)]
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
        var forwarder = new SubprocessLogForwarder(logger);

        forwarder.Forward("");
        forwarder.Forward("   ");
        forwarder.Forward(null!);

        Assert.Empty(logger.Entries);
    }

    [Fact]
    public void Forward_SingleLine_LogsAtCorrectLevel()
    {
        var logger = new FakeLogger();
        var forwarder = new SubprocessLogForwarder(logger);

        forwarder.Forward("warn: MyApp.Service[0] Something is wrong");

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, logger.Entries[0].Level);
        Assert.Contains("Something is wrong", logger.Entries[0].Message);
    }

    [Fact]
    public void Forward_MultipleLines_EachAtCorrectLevel()
    {
        var logger = new FakeLogger();
        var forwarder = new SubprocessLogForwarder(logger);

        string output = string.Join(Environment.NewLine, new[]
        {
            "info: MyApp.Upload[0] Upload started",
            "warn: MyApp.Upload[0] Retrying upload",
            "fail: MyApp.Upload[0] Upload failed",
        });

        forwarder.Forward(output);

        Assert.Equal(3, logger.Entries.Count);
        Assert.Equal(LogLevel.Debug, logger.Entries[0].Level);     // info → Debug
        Assert.Equal(LogLevel.Warning, logger.Entries[1].Level);
        Assert.Equal(LogLevel.Error, logger.Entries[2].Level);
    }

    [Fact]
    public void Forward_ContinuationLines_InheritPreviousLevel()
    {
        var logger = new FakeLogger();
        var forwarder = new SubprocessLogForwarder(logger);

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
        var forwarder = new SubprocessLogForwarder(logger);

        string output = "info: MyApp[0] First\n\n\nwarn: MyApp[0] Second";

        forwarder.Forward(output);

        Assert.Equal(2, logger.Entries.Count);
    }

    /// <summary>
    /// Simple in-memory logger for testing.
    /// </summary>
    private sealed class FakeLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
