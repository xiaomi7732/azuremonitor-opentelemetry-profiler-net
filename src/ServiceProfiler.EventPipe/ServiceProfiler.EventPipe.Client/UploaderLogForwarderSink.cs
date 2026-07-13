//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Profiler.Core.Logging;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.Extensions.Logging;

namespace Microsoft.ApplicationInsights.Profiler.Core.Orchestration;

/// <summary>
/// Forwards uploader sub-process log lines to the customer's Application Insights using the
/// profiler's dedicated telemetry channel (<see cref="IAppInsightsLogger"/>). The classic
/// profiler's <see cref="ILogger"/> pipeline is not connected to the customer's resource, so this
/// sink is required for uploader logs to appear there.
/// </summary>
internal sealed class UploaderLogForwarderSink : IUploaderLogForwarderSink
{
    private readonly IEnumerable<IAppInsightsLogger> _loggers;

    public UploaderLogForwarderSink(IEnumerable<IAppInsightsLogger> loggers)
    {
        _loggers = loggers ?? throw new ArgumentNullException(nameof(loggers));
    }

    public void Track(LogLevel level, string message)
    {
        SeverityLevel severityLevel = ToSeverityLevel(level);
        foreach (IAppInsightsLogger logger in _loggers)
        {
            logger.TrackTrace(message, severityLevel);
        }
    }

    private static SeverityLevel ToSeverityLevel(LogLevel level) => level switch
    {
        LogLevel.Critical => SeverityLevel.Critical,
        LogLevel.Error => SeverityLevel.Error,
        LogLevel.Warning => SeverityLevel.Warning,
        LogLevel.Information => SeverityLevel.Information,
        _ => SeverityLevel.Verbose,
    };
}
