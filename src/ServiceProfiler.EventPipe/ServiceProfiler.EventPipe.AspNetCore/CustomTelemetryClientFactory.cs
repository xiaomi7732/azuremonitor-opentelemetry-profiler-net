//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using System;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights.Extensibility.Implementation;
using Microsoft.ApplicationInsights.Profiler.Core.SampleTransfers;
using Microsoft.ServiceProfiler.Utilities;

namespace Microsoft.ApplicationInsights.Profiler.AspNetCore
{
    // TODO: Look into is this needed? Shall we directly inject TelemetryClient when needed?
    internal sealed class CustomTelemetryClientFactory : ICustomTelemetryClientFactory
    {
        private readonly TelemetryConfiguration _telemetryConfiguration;

        public CustomTelemetryClientFactory(TelemetryConfiguration telemetryConfiguration)
        {
            _telemetryConfiguration = telemetryConfiguration ?? throw new ArgumentNullException(nameof(telemetryConfiguration));
        }

        public TelemetryClient Create()
        {
            var telemetryClient = new TelemetryClient(_telemetryConfiguration);
            string sdkVersion = EnvironmentUtilities.GetApplicationInsightsSdkVersion("l_ap:");
            telemetryClient.Context.GetInternalContext().SdkVersion = sdkVersion;
            return telemetryClient;
        }
    }
}
