//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using System;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights.Profiler.Core.SampleTransfers;

namespace Microsoft.ApplicationInsights.Profiler.Core.Stubs
{
    internal sealed class CustomTelemetryClientStubFactory : ICustomTelemetryClientFactory, IDisposable
    {
        private TelemetryConfiguration _telemetryConfiguration;
        private ITelemetryChannel _telemetryChannelStub;
        public CustomTelemetryClientStubFactory(string iKey, ITelemetryChannel telemetryChannel = null)
        {
            _telemetryChannelStub = telemetryChannel ?? new TelemetryChannelStub();
            _telemetryConfiguration = new TelemetryConfiguration
            {
                ConnectionString = $"InstrumentationKey={iKey}",
                TelemetryChannel = telemetryChannel ?? _telemetryChannelStub
            };
        }

        public TelemetryClient Create()
        {
            return new TelemetryClient(_telemetryConfiguration);
        }

        public void Dispose()
        {
            _telemetryConfiguration?.Dispose();
            _telemetryConfiguration = null;

            _telemetryChannelStub?.Dispose();
            _telemetryChannelStub = null;
        }
    }
}
