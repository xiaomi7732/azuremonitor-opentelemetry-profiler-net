//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using System.Diagnostics.Tracing;
using Azure.Monitor.OpenTelemetry.Profiler.Core.EventListeners;
using Microsoft.ApplicationInsights.Profiler.Shared.Contracts;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Azure.Monitor.OpenTelemetry.Profiler.Core;

internal class DiagnosticsClientTraceConfiguration : DiagnosticsClientTraceConfigurationBase
{
    public DiagnosticsClientTraceConfiguration(
        IOptions<UserConfigurationBase> userConfiguration,
        ILogger<DiagnosticsClientTraceConfigurationBase> logger)
        : base(userConfiguration, logger)
    {
    }

    /// <inheritdoc />
    protected override IEnumerable<EventPipeProvider> AppendEventPipeProviders()
    {
        // Open Telemetry Profiler Data adapter event source so that trace analysis knows about the activities
        yield return new EventPipeProvider(AzureMonitorOpenTelemetryProfilerDataAdapterEventSource.EventSourceName, EventLevel.Verbose, keywords: 0xffffffffffff, arguments: null);
    }
}