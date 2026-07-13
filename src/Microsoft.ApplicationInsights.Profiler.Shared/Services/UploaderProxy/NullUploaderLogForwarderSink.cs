// -----------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
// -----------------------------------------------------------------------------

using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.Extensions.Logging;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services.UploaderProxy;

/// <summary>
/// No-op <see cref="IUploaderLogForwarderSink"/> for hosts whose <see cref="ILogger"/> pipeline
/// already forwards uploader logs to the customer's Application Insights (e.g. the OpenTelemetry
/// profiler). Forwarding again would duplicate the telemetry, so this sink does nothing.
/// </summary>
internal sealed class NullUploaderLogForwarderSink : IUploaderLogForwarderSink
{
    public void Track(LogLevel level, string message)
    {
        // Intentionally no-op: the host's ILogger pipeline already routes these logs to
        // Application Insights.
    }
}
