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
    private readonly ILogger _logger;

    public DiagnosticsClientTraceConfiguration(
        IOptions<UserConfigurationBase> userConfiguration,
        ILogger<DiagnosticsClientTraceConfigurationBase> logger)
        : base(userConfiguration, logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    protected override IEnumerable<EventPipeProvider> AppendEventPipeProviders()
    {
        // Gate the EventPipe providers by the same request-source kill switch used by
        // TraceSessionListenerFactory. This avoids enabling (and serializing through EventPipe)
        // providers whose events would be dropped by the in-process handler selection.
        RequestSourceMode mode = RequestSourceModeResolver.Resolve(_logger);

        // Open Telemetry SDK Event Source — only needed when the OTel-SDK handler is active.
        if (mode is RequestSourceMode.OpenTelemetrySdk or RequestSourceMode.Both)
        {
            yield return new EventPipeProvider(TraceSessionListener.OpenTelemetrySDKEventSourceName, EventLevel.Verbose, keywords: 0xffffffffffff, arguments: null);
        }

        // Diagnostic Source Event Source — only needed when the DS handler is active.
        // Narrow FilterAndPayloadSpecs to the exact ActivitySources we care about (ASP.NET Core HTTP-in
        // and Azure Service Bus processor) to match the handler; [AS]* would firehose every ActivitySource
        // in the process (EF Core, HttpClient, custom sources, ...).
        if (mode is RequestSourceMode.DiagnosticSource or RequestSourceMode.Both)
        {
            yield return new EventPipeProvider(TraceSessionListener.DiagnosticSourceEventSourceName, EventLevel.Verbose, keywords: 0xffffffffffff, arguments: new Dictionary<string, string>
            {
                ["FilterAndPayloadSpecs"] = DiagnosticSourceEventSourceHandler.FilterAndPayloadSpecs,
            });
        }

        // Open Telemetry Profiler Data adapter event source so that trace analysis knows about the activities.
        // Always on — this is our own emitter.
        yield return new EventPipeProvider(AzureMonitorOpenTelemetryProfilerDataAdapterEventSource.EventSourceName, EventLevel.Verbose, keywords: 0xffffffffffff, arguments: null);
    }
}