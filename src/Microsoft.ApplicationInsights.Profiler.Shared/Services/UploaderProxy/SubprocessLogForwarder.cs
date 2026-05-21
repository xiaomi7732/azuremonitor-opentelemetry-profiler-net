// -----------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
// -----------------------------------------------------------------------------

using System;
using Microsoft.Extensions.Logging;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services.UploaderProxy;

/// <summary>
/// Parses structured console output (SimpleConsole single-line format) from a subprocess
/// and re-emits each line through <see cref="ILogger"/> at the corresponding log level.
/// </summary>
/// <remarks>
/// The SimpleConsole formatter with SingleLine=true produces lines like:
///   info: Namespace.Class[0] Message text
///   warn: Namespace.Class[0] Warning message
///   fail: Namespace.Class[0] Error message
///
/// This class recognizes the standard prefixes (trce, dbug, info, warn, fail, crit)
/// and maps them to <see cref="LogLevel"/> values. Continuation lines (no recognized prefix)
/// inherit the level of the preceding line.
/// </remarks>
internal sealed class SubprocessLogForwarder
{
    private readonly ILogger _logger;

    public SubprocessLogForwarder(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Splits <paramref name="output"/> into lines and logs each at the level
    /// indicated by its prefix.
    /// </summary>
    public void Forward(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return;
        }

        LogLevel currentLevel = LogLevel.Debug;

        foreach (string line in output.Split('\n'))
        {
            ReadOnlySpan<char> trimmed = line.AsSpan().TrimEnd('\r');
            if (trimmed.IsWhiteSpace())
            {
                continue;
            }

            LogLevel parsed = ParseLogLevel(trimmed);
            if (parsed != LogLevel.None)
            {
                currentLevel = parsed;
            }

            _logger.Log(currentLevel, "[Uploader] {line}", trimmed.ToString());
        }
    }

    /// <summary>
    /// Parses the log level from a Systemd/SimpleConsole-formatted line.
    /// Returns <see cref="LogLevel.None"/> if no recognized prefix is found.
    /// </summary>
    internal static LogLevel ParseLogLevel(ReadOnlySpan<char> line)
    {
        // The prefix format is "level: " where level is exactly 4 chars.
        // Minimum valid line: "info: X" = 7 chars.
        if (line.Length < 6)
        {
            return LogLevel.None;
        }

        // Check for the colon at position 4.
        if (line[4] != ':')
        {
            return LogLevel.None;
        }

        ReadOnlySpan<char> prefix = line.Slice(0, 4);

        if (prefix.SequenceEqual("crit".AsSpan()))
        {
            return LogLevel.Critical;
        }
        if (prefix.SequenceEqual("fail".AsSpan()))
        {
            return LogLevel.Error;
        }
        if (prefix.SequenceEqual("warn".AsSpan()))
        {
            return LogLevel.Warning;
        }
        if (prefix.SequenceEqual("info".AsSpan()))
        {
            return LogLevel.Information;
        }
        if (prefix.SequenceEqual("dbug".AsSpan()))
        {
            return LogLevel.Debug;
        }
        if (prefix.SequenceEqual("trce".AsSpan()))
        {
            return LogLevel.Trace;
        }

        return LogLevel.None;
    }
}
