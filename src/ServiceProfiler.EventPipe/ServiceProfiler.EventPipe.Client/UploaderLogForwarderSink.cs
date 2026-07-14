//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using System;
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
/// <remarks>
/// Only the customer's logger is targeted (never Microsoft's anonymous-telemetry logger): uploader
/// stdout can contain customer-specific details (paths, machine/role names, blob URIs, errors) and
/// must not be sent to Microsoft's resource.
/// </remarks>
internal sealed class UploaderLogForwarderSink : IUploaderLogForwarderSink
{
    private readonly IAppInsightsLogger _customerLogger;

    public UploaderLogForwarderSink(CustomerAppInsightsLogger customerLogger)
    {
        _customerLogger = customerLogger?.Logger ?? throw new ArgumentNullException(nameof(customerLogger));
    }

    public void Track(LogLevel level, string message)
    {
        _customerLogger.TrackTrace(message, ToSeverityLevel(level));
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
