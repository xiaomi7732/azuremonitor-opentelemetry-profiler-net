// -----------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;

/// <summary>
/// Forwards uploader (sub-process) log lines to the customer's Application Insights resource.
/// </summary>
/// <remarks>
/// The uploader runs out-of-process; its logs are captured and re-emitted through
/// <see cref="ILogger"/>. In hosts where the <see cref="ILogger"/> pipeline is already wired to
/// the customer's Application Insights (e.g. the OpenTelemetry profiler), no extra forwarding is
/// needed and a no-op implementation is used. In the classic profiler the <see cref="ILogger"/>
/// pipeline is not connected to the customer resource, so an implementation backed by the
/// profiler's dedicated telemetry channel forwards the lines instead.
/// </remarks>
internal interface IUploaderLogForwarderSink
{
    /// <summary>
    /// Tracks a single uploader log line at the given <paramref name="level"/> to the customer's
    /// Application Insights resource.
    /// </summary>
    void Track(LogLevel level, string message);
}
