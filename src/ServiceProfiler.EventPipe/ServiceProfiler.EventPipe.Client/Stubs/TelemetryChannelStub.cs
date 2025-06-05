//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using Microsoft.ApplicationInsights.Channel;

namespace Microsoft.ApplicationInsights.Profiler.Core.Stubs
{
    internal class TelemetryChannelStub : ITelemetryChannel
    {
        public TelemetryChannelStub(string endpointAddress = "http://localhost:9898/v2/track", bool developMode = true)
        {
            DeveloperMode = developMode;
            EndpointAddress = endpointAddress;
        }

        public bool? DeveloperMode { get; set; }
        public string EndpointAddress { get; set; }

        public void Dispose() { }

        public void Flush() { }

        public void Send(ITelemetry item)
        {
            // Add to a collection when items need to be verified.
        }
    }
}