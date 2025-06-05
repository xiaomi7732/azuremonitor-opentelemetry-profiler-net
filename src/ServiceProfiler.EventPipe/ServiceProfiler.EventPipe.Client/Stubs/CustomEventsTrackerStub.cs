//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Profiler.Core.SampleTransfers;
using Microsoft.ApplicationInsights.Profiler.Core.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.ServiceProfiler.Orchestration;
using System;

namespace Microsoft.ApplicationInsights.Profiler.Core.Stubs
{
    internal class CustomEventsTrackerStub : CustomEventsTracker, ICustomEventsTracker
    {
        public CustomEventsTrackerStub(
            IServiceProfilerContext serviceProfilerContext,
            ICustomTelemetryClientFactory customTelemetryClientFactory,
            IResourceUsageSource resourceUsage,
            ISerializationProvider serializer,
            ILogger<CustomEventsTrackerStub> logger)
            : base(serviceProfilerContext, customTelemetryClientFactory, resourceUsage, serializer, logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override void SendCustomEventToAI(EventTelemetry telemetry)
        {
            bool arePropertiesSerialized = _serializer.TrySerialize(telemetry.Properties, out string serializedProperties);
            // Do not really send because this is a stub.
            _logger.LogInformation("[Stub] Sending custom events to AI:" + Environment.NewLine + "{0}", serializedProperties);
        }

        private readonly ILogger _logger;
    }
}
